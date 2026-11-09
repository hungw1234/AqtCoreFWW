using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Infrastructure.Backup;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// System backup/restore admin endpoints â€” NÄ53 compliance.
/// Triggers Hangfire background jobs for backup/restore drill operations.
/// Requires SystemAdmin role.
/// </summary>
[Route("api/v1/admin/backup")]
[Authorize(Policy = PermCodes.System.Config.Manage)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class BackupAdminController(
    BackupDbContext db,
    IGuidGenerator guidGen,
    IBackgroundJobService jobService) : ApiControllerBase
{
    /// <summary>List backup/restore records (most recent first).</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (Math.Max(page, 1) - 1) * pageSize;

        var totalCount = await db.BackupRecords.CountAsync(ct);
        var records = await db.BackupRecords
            .OrderByDescending(r => r.StartedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(r => new BackupRecordDto(
                r.Id, r.Type, r.Status, r.FilePath, r.FileSizeBytes,
                r.ErrorMessage, r.TriggeredBy, r.StartedAt, r.CompletedAt))
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { items = records, totalCount }));
    }

    /// <summary>Trigger a new database backup (async via Hangfire).</summary>
    [HttpPost]
    [ProducesResponseType(202)]
    public async Task<IActionResult> TriggerBackup(CancellationToken ct)
    {
        var userId = GetUserId();
        var record = BackupRecord.Create(guidGen, "Backup", userId);
        db.BackupRecords.Add(record);
        await db.SaveChangesAsync(ct);

        // Enqueue Hangfire job
        jobService.Enqueue<DatabaseBackupService>(svc => svc.ExecuteBackupAsync(record.Id));

        return Accepted(ApiResponse<object>.Ok(new { recordId = record.Id, status = "Pending" }));
    }

    /// <summary>Trigger restore drill â€” backup + restore to temp DB + verify + cleanup (NÄ53).</summary>
    [HttpPost("restore-drill")]
    [ProducesResponseType(202)]
    public async Task<IActionResult> TriggerRestoreDrill(CancellationToken ct)
    {
        var userId = GetUserId();
        var record = BackupRecord.Create(guidGen, "RestoreDrill", userId);
        db.BackupRecords.Add(record);
        await db.SaveChangesAsync(ct);

        // Enqueue Hangfire job
        jobService.Enqueue<DatabaseBackupService>(svc => svc.ExecuteRestoreDrillAsync(record.Id));

        return Accepted(ApiResponse<object>.Ok(new { recordId = record.Id, status = "Pending" }));
    }

    /// <summary>Get a single backup record by ID (poll status).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var record = await db.BackupRecords.FindAsync([id], ct);
        if (record is null) return NotFound();

        return Ok(ApiResponse<BackupRecordDto>.Ok(new BackupRecordDto(
            record.Id, record.Type, record.Status, record.FilePath, record.FileSizeBytes,
            record.ErrorMessage, record.TriggeredBy, record.StartedAt, record.CompletedAt)));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public sealed record BackupRecordDto(
    Guid Id, string Type, string Status, string? FilePath, long? FileSizeBytes,
    string? ErrorMessage, Guid TriggeredBy, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);
