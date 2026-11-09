using System.Net;
using System.Net.Sockets;
using System.Text;
using AqtCoreFW.Files.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Minio;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Files.Infrastructure.Tests.Storage;

/// <summary>
/// DownloadAsync against a loopback S3 stub, so the real Minio SDK request/response handling runs.
/// Pins the case seen on a shared environment: the storage account may HEAD the object but its GET is
/// refused with 403 AccessDenied. Minio SDK 6.x then passes the S3 error XML to the callback stream
/// instead of throwing; DownloadAsync must throw rather than return that XML as the file content
/// (otherwise downloads serve the XML and the virus scan certifies it clean).
/// </summary>
public sealed class MinioFileStorageServiceDownloadTests : IAsyncDisposable
{
    private const string Bucket = "test-bucket";
    private const string Key = "tenant/object.txt";

    private static readonly byte[] ObjectBytes =
        Encoding.ASCII.GetBytes("stored object content used by the download integrity tests\n");

    private static readonly byte[] AccessDeniedXml = Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Error><Code>AccessDenied</Code>" +
        "<Message>Access Denied.</Message><Key>tenant/object.txt</Key><BucketName>test-bucket</BucketName>" +
        "<Resource>/test-bucket/tenant/object.txt</Resource><RequestId>1</RequestId><HostId>1</HostId></Error>");

    private readonly HttpListener _listener = new();
    private readonly int _port = GetFreePort();
    private Func<HttpListenerRequest, (int Status, byte[] Body, string ContentType)>? _getResponse;
    private Task? _serveLoop;

    [Fact]
    public async Task DownloadAsync_GetRefusedAfterHeadAllowed_ThrowsInsteadOfReturningErrorXml()
    {
        StartStub(_ => (403, AccessDeniedXml, "application/xml"));

        var act = () => CreateService().DownloadAsync(Bucket, Key);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage($"*{AccessDeniedXml.Length} bytes*{ObjectBytes.Length} bytes*");
    }

    [Fact]
    public async Task DownloadAsync_GetAllowed_ReturnsObjectBytesFromStart()
    {
        StartStub(_ => (200, ObjectBytes, "text/plain"));

        await using var stream = await CreateService().DownloadAsync(Bucket, Key);

        stream.Position.Should().Be(0);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        copy.ToArray().Should().Equal(ObjectBytes);
    }

    private MinioFileStorageService CreateService()
    {
        var client = new MinioClient()
            .WithEndpoint("127.0.0.1", _port)
            .WithCredentials("test-access", "test-secret")
            .WithSSL(false)
            .Build();
        return new MinioFileStorageService(
            client, new RecyclableMemoryStreamManager(), Substitute.For<ILogger<MinioFileStorageService>>());
    }

    /// <summary>HEAD always reports the object (size = ObjectBytes); GET answers with <paramref name="get"/>.</summary>
    private void StartStub(Func<HttpListenerRequest, (int, byte[], string)> get)
    {
        _getResponse = get;
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _serveLoop = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) { return; } // listener stopped

                var res = ctx.Response;
                if (ctx.Request.HttpMethod == "HEAD")
                {
                    res.StatusCode = 200;
                    res.ContentLength64 = ObjectBytes.Length;
                    res.ContentType = "text/plain";
                    res.Headers["ETag"] = "\"0123456789abcdef\"";
                    res.Headers["Last-Modified"] = DateTime.UtcNow.ToString("R");
                }
                else
                {
                    var (status, body, contentType) = _getResponse!(ctx.Request);
                    res.StatusCode = status;
                    res.ContentType = contentType;
                    res.ContentLength64 = body.Length;
                    await res.OutputStream.WriteAsync(body);
                }

                res.Close();
            }
        });
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        if (_serveLoop is not null) await _serveLoop;
    }
}
