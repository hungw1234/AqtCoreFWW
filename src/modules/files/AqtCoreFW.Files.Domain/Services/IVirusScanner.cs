namespace AqtCoreFW.Files.Domain.Services;

/// <summary>
/// ClamAV virus scanner abstraction.
/// Fail-closed: an unreachable clamd daemon yields <see cref="VirusScanStatus.Error"/> (never Clean),
/// so the caller can retry rather than pass an unscanned file.
/// </summary>
public interface IVirusScanner
{
    /// <summary>Scan stream for viruses. Returns result with virus name if infected.</summary>
    Task<VirusScanResult> ScanAsync(Stream stream, CancellationToken cancellationToken = default);
}

public sealed record VirusScanResult(VirusScanStatus Status, string? VirusName = null)
{
    public static VirusScanResult Clean() => new(VirusScanStatus.Clean);
    public static VirusScanResult Infected(string virusName) => new(VirusScanStatus.Infected, virusName);
    public static VirusScanResult Error(string reason) => new(VirusScanStatus.Error, reason);
}

public enum VirusScanStatus { Clean, Infected, Error }
