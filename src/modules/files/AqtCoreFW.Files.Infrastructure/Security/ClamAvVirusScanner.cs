using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Files.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nClam;

namespace AqtCoreFW.Files.Infrastructure.Security;

/// <summary>
/// ClamAV virus scanner via TCP connection to clamd daemon (nClam library).
/// Fail-closed: when clamd is unreachable the scan returns Error (never Clean), so the caller retries
/// rather than passing an unscanned file. Whether scanning runs at all is gated upstream by the
/// feature:files.virus-scan flag — this scanner is only invoked when scanning is enabled.
/// Scan timeout: 30s (NF2 requirement).
/// </summary>
public sealed class ClamAvVirusScanner(
    IOptions<ClamAvOptions> options,
    ILogger<ClamAvVirusScanner> logger) : IVirusScanner
{
    public async Task<VirusScanResult> ScanAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;

        try
        {
            var client = new ClamClient(opts.Host, opts.Port)
            {
                MaxStreamSize = 200 * 1024 * 1024 // 200 MB safety cap
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(opts.TimeoutMs));

            stream.Position = 0;
            var result = await client.SendAndScanFileAsync(stream, cts.Token);

            return result.Result switch
            {
                ClamScanResults.Clean => VirusScanResult.Clean(),
                ClamScanResults.VirusDetected => VirusScanResult.Infected(
                    result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown"),
                _ => VirusScanResult.Error($"ClamAV returned: {result.Result}")
            };
        }
        catch (Exception ex)
        {
            // Fail-closed: an unreachable/erroring clamd yields Error (not Clean), so ClamAvScanJob
            // retries and the file stays Quarantined rather than passing unscanned.
            logger.LogError(ex, "ClamAV scan failed. Host={Host}:{Port}", opts.Host, opts.Port);
            return VirusScanResult.Error(ex.Message);
        }
    }
}
