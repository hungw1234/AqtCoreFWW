using System.Text.Json;
using AqtCoreFW.Infrastructure.Middleware;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Security.Tests.Leakage;

/// <summary>
/// Error response leakage tests — TC-SEC-LEAK-001, TC-SEC-LEAK-003.
/// Ensures no stack traces or DB internals in production error responses.
/// </summary>
public sealed class ErrorResponseLeakageTests
{
    private static GlobalExceptionHandlerMiddleware CreateMiddleware(
        RequestDelegate throwingDelegate, bool isDevelopment = false)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        var guidGen = Substitute.For<IGuidGenerator>();
        guidGen.NewIdRandom().Returns(Guid.NewGuid());
        // IHostEnvironment.IsDevelopment() is an extension method checking EnvironmentName
        return new GlobalExceptionHandlerMiddleware(
            throwingDelegate,
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance,
            env,
            guidGen);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Production_NoStackTrace_InErrorResponse()
    {
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("SELECT * FROM Users WHERE Id = @id"),
            isDevelopment: false);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        body.Should().NotContain("SELECT");
        body.Should().NotContain("StackTrace");
        body.Should().NotContain("at AqtCoreFW");
        body.Should().Contain("Contact support");
        context.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Production_NoConnectionString_InErrorResponse()
    {
        var middleware = CreateMiddleware(
            _ => throw new Exception("Server=localhost;Database=AqtDb;User=sa;Password=secret123"),
            isDevelopment: false);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        body.Should().NotContain("Password");
        body.Should().NotContain("Server=");
        body.Should().NotContain("secret123");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ErrorResponse_ContainsCorrelationId()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("test"), isDevelopment: false);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("correlationId", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("errorCode", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ArgumentException_Returns400_NotStackTrace()
    {
        var middleware = CreateMiddleware(
            _ => throw new ArgumentException("Invalid CaseId format"),
            isDevelopment: false);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().NotContain("CaseId format");
        body.Should().Contain("Invalid request parameter");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnauthorizedException_Returns401()
    {
        var middleware = CreateMiddleware(
            _ => throw new UnauthorizedAccessException("Token expired"),
            isDevelopment: false);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    // Feature stub that reports the response as already started (as a streaming endpoint would after
    // flushing headers). DefaultHttpContext's own response feature always reports HasStarted=false.
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = 200;
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ResponseAlreadyStarted_RethrowsInsteadOfCorruptingBody()
    {
        // A streaming endpoint (e.g. the Forms CSV export) that fails mid-stream: the response has
        // already started, so the middleware must NOT try to rewrite the status/body — it re-throws
        // and lets the host abort the connection, leaving a truncated (not corrupted) download.
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("db dropped at row 5000"),
            isDevelopment: false);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var act = async () => await middleware.InvokeAsync(context);

        // The original exception propagates out — the middleware does not swallow it or write JSON.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*row 5000*");
    }
}
