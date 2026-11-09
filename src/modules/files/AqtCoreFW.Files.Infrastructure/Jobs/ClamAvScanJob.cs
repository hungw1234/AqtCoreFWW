using AqtCoreFW.Files.Application.Jobs;
using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Infrastructure.Jobs;

/// <summary>
/// Phase 2 of two-phase upload: async Hangfire job for ClamAV virus scan. Three-way verdict:
///   Clean    → FileStatus.Available (file accessible for download).
///   Infected → FileStatus.Rejected + delete from MinIO (file permanently removed).
///   Error    → throw so Hangfire retries; file stays Quarantined, storage untouched (infra fault,
///              not a verdict — the scanner is fail-closed, never passes an unscanned file).
/// Executed in the Hangfire "virus-scan" queue with retry on transient errors.
/// Gated by feature:files.virus-scan flag — when disabled, auto-passes (dev/demo mode).
/// Phase-02c: guidGen injected for Rule1-compliant event MessageId in MarkAvailable/MarkRejected.
/// </summary>
public sealed class ClamAvScanJob(
    IFileRepository fileRepository,
    IFileStorageService storageService,
    IVirusScanner virusScanner,
    IFeatureFlagService featureFlags,
    IGuidGenerator guidGen,
    IOptions<FilesOptions> options,
    ILogger<ClamAvScanJob> logger) : IClamAvScanJob
{
    public async Task ScanAsync(Guid fileId)
    {
        var fileResult = await fileRepository.GetByIdAsync(FileId.From(fileId));
        if (fileResult.IsFailed)
        {
            logger.LogWarning("ClamAvScanJob: FileRecord {FileId} not found.", fileId);
            return;
        }

        var file = fileResult.Value;

        if (file.Status != FileStatus.Quarantined)
        {
            logger.LogInformation(
                "ClamAvScanJob: File {FileId} already processed (status={Status}). Skipping.",
                fileId, file.Status);
            return;
        }

        // Feature flag gate: skip virus scan when disabled (dev/demo mode)
        // SECURITY: In production, feature:files.virus-scan MUST be enabled (NĐ68 compliance).
        // Read GLOBAL (no tenant): malware scanning is deployment-wide posture, not a per-tenant
        // capability — a tenant must not be able to opt its own uploads out of it. The toggle endpoint
        // enforces the same by writing this flag to the global row only (FeatureFlagsController).
        if (!featureFlags.IsEnabled("feature:files.virus-scan"))
        {
            file.MarkAvailable(guidGen);
            await fileRepository.UpdateAsync(file);
            logger.LogCritical(
                "SECURITY BYPASS: Virus scan disabled by feature flag. File {FileId} marked Available WITHOUT scan. " +
                "Ensure feature:files.virus-scan is enabled in production.",
                fileId);
            return;
        }

        try
        {
            // Download from MinIO for scanning
            await using var stream = await storageService.DownloadAsync(
                options.Value.BucketName, file.StorageKey);

            var scanResult = await virusScanner.ScanAsync(stream);

            if (scanResult.Status == VirusScanStatus.Clean)
            {
                file.MarkAvailable(guidGen);
                await fileRepository.UpdateAsync(file);
                logger.LogInformation(
                    "ClamAvScanJob: File {FileId} scan PASSED → Available.", fileId);
            }
            else if (scanResult.Status == VirusScanStatus.Infected)
            {
                file.MarkRejected(guidGen);
                await fileRepository.UpdateAsync(file);

                // Delete from MinIO — infected file must not remain in storage
                await storageService.DeleteAsync(options.Value.BucketName, file.StorageKey);

                logger.LogWarning(
                    "ClamAvScanJob: File {FileId} scan FAILED (virus={VirusName}) → Rejected + deleted from storage.",
                    fileId, scanResult.VirusName);
            }
            else
            {
                // VirusScanStatus.Error: the scanner produced no verdict (clamd unreachable, timed out, or a
                // protocol error). This is an infrastructure fault, NOT evidence the file is malicious — do NOT
                // reject+delete a possibly-clean file on it. Throw so Hangfire retries; the record stays
                // Quarantined and the object stays in storage until a real Clean/Infected verdict lands.
                // VirusScanResult.Error carries its reason in the VirusName slot.
                throw new InvalidOperationException(
                    $"ClamAV could not scan file {fileId}: {scanResult.VirusName ?? "unknown scanner error"}.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ClamAvScanJob: Unhandled error scanning file {FileId}.", fileId);
            throw; // Hangfire retries on exception
        }
    }
}
