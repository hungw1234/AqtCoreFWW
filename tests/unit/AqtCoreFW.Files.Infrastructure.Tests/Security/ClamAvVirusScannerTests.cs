using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Files.Infrastructure.Options;
using AqtCoreFW.Files.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Infrastructure.Tests.Security;

/// <summary>
/// Unit tests for ClamAvVirusScanner's fail-closed contract.
///
/// The scanner has no bypass: when clamd is unreachable it MUST return Error (so ClamAvScanJob retries
/// and the file stays Quarantined), and it must never return Clean on an infrastructure failure or let
/// the exception escape. This is verified by pointing the scanner at a closed local port.
/// </summary>
public sealed class ClamAvVirusScannerTests
{
    private static ClamAvVirusScanner ScannerFor(string host, int port) =>
        new(
            // Fully-qualified: `using AqtCoreFW.Files.Infrastructure.Options` shadows Microsoft.Extensions.Options.Options.
            Microsoft.Extensions.Options.Options.Create(new ClamAvOptions { Host = host, Port = port, TimeoutMs = 2000 }),
            NullLogger<ClamAvVirusScanner>.Instance);

    [Fact]
    public async Task ScanAsync_ClamdUnreachable_ReturnsError_NotClean()
    {
        // 127.0.0.1:1 is never a listening clamd — the TCP connect is refused fast.
        var scanner = ScannerFor("127.0.0.1", 1);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var result = await scanner.ScanAsync(stream);

        result.Status.Should().Be(VirusScanStatus.Error,
            "an unreachable clamd is an infrastructure fault, not a Clean verdict (fail-closed)");
    }

    [Fact]
    public async Task ScanAsync_ClamdUnreachable_DoesNotThrow()
    {
        var scanner = ScannerFor("127.0.0.1", 1);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var act = async () => await scanner.ScanAsync(stream);

        // The exception is swallowed into an Error result so ClamAvScanJob's retry path drives the outcome.
        await act.Should().NotThrowAsync();
    }
}
