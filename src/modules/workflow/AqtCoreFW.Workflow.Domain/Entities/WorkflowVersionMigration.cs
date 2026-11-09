using System.Security.Cryptography;
using System.Text;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Represents an admin-initiated batch migration of workflow instances
/// from one definition version to another.
/// Status flow: PendingApproval → Approved → InProgress → Completed/Failed/Cancelled.
/// MappingHash (SHA-256) locked at creation; re-verified at Execute to detect tampering.
/// CancelRequested polled by Hangfire worker between batches.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   SourceDefinitionId: long, TargetDefinitionId: long (FKs to T2 WorkflowDefinition.Id).
///   ApprovedBy: Guid? (FK exception — user ref).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class WorkflowVersionMigration : AuditableEntity, ITenantScoped
{
    public long SourceDefinitionId { get; private set; }
    public long TargetDefinitionId { get; private set; }

    /// <summary>JSON: [{oldStateId, newStateId}]. Immutable after PendingApproval.</summary>
    public string StateMappingsJson { get; private set; } = string.Empty;

    /// <summary>SHA-256 hex of StateMappingsJson, stored at creation.</summary>
    public string MappingHash { get; private set; } = string.Empty;

    public MigrationStatus Status { get; private set; }
    public int TotalInstances { get; private set; }
    public int MigratedCount { get; private set; }
    public int FailedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool CancelRequested { get; private set; }
    public string? HangfireJobId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>EF Core concurrency token.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowVersionMigration() { } // EF Core

    /// <summary>
    /// Creates a migration starting at PendingApproval (v1: Draft skipped per KISS).
    /// Immediately computes and locks the MappingHash.
    /// Guards use > 0 check for long PKs instead of Guid.Empty.
    /// </summary>
    public static WorkflowVersionMigration Create(
        long sourceDefinitionId,
        long targetDefinitionId,
        string stateMappingsJson,
        Guid tenantId,
        Guid createdBy)
    {
        if (sourceDefinitionId <= 0)
            throw new ArgumentException("SourceDefinitionId must be positive.", nameof(sourceDefinitionId));
        if (targetDefinitionId <= 0)
            throw new ArgumentException("TargetDefinitionId must be positive.", nameof(targetDefinitionId));
        if (sourceDefinitionId == targetDefinitionId)
            throw new ArgumentException("Source and target definition must differ.");
        if (string.IsNullOrWhiteSpace(stateMappingsJson))
            throw new ArgumentException("StateMappingsJson must not be empty.", nameof(stateMappingsJson));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));

        var hash = ComputeHash(stateMappingsJson);
        var migration = new WorkflowVersionMigration
        {
            SourceDefinitionId = sourceDefinitionId,
            TargetDefinitionId = targetDefinitionId,
            StateMappingsJson = stateMappingsJson,
            MappingHash = hash,
            Status = MigrationStatus.PendingApproval,
            TenantId = tenantId
        };
        migration.SetAuditCreate(createdBy);
        return migration;
    }

    /// <summary>Recompute SHA-256 of current StateMappingsJson for comparison.</summary>
    public string ComputeMappingHash() => ComputeHash(StateMappingsJson);

    /// <summary>
    /// Verify stored MappingHash matches the current JSON.
    /// Throws if tampered — called at Execute pre-check.
    /// </summary>
    public void VerifyMappingHash()
    {
        if (ComputeMappingHash() != MappingHash)
            throw new InvalidOperationException(
                "StateMappingsJson has been tampered with — hash mismatch. Migration aborted.");
    }

    /// <summary>Approve migration. Status must be PendingApproval.</summary>
    public void MarkApproved(Guid approvedBy)
    {
        if (Status != MigrationStatus.PendingApproval)
            throw new InvalidOperationException(
                $"Cannot approve migration in status {Status}. Must be PendingApproval.");

        Status = MigrationStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark as InProgress when Hangfire job starts. Status must be Approved.</summary>
    public void MarkStarted(string hangfireJobId)
    {
        if (Status != MigrationStatus.Approved)
            throw new InvalidOperationException(
                $"Cannot start migration in status {Status}. Must be Approved.");

        Status = MigrationStatus.InProgress;
        HangfireJobId = hangfireJobId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark as Completed. Status must be InProgress.</summary>
    public void MarkCompleted()
    {
        if (Status != MigrationStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot complete migration in status {Status}. Must be InProgress.");

        Status = MigrationStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark as Failed. Status must be InProgress.</summary>
    public void MarkFailed()
    {
        if (Status != MigrationStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot fail migration in status {Status}. Must be InProgress.");

        Status = MigrationStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Mark as Cancelled immediately.
    /// Valid from PendingApproval (no lock yet), Approved (not yet started), or InProgress (Hangfire path).
    /// For InProgress, caller should prefer RequestCancel() so the worker can flush the current batch first.
    /// </summary>
    public void MarkCancelled()
    {
        if (Status is not (MigrationStatus.PendingApproval
            or MigrationStatus.Approved
            or MigrationStatus.InProgress))
            throw new InvalidOperationException(
                $"Cannot cancel migration in status {Status}. Must be PendingApproval, Approved, or InProgress.");

        Status = MigrationStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Signal cancellation. Valid from PendingApproval, Approved, or InProgress.
    /// InProgress worker polls CancelRequested between batches.
    /// </summary>
    public void RequestCancel()
    {
        if (Status is not (MigrationStatus.PendingApproval
            or MigrationStatus.Approved
            or MigrationStatus.InProgress))
            throw new InvalidOperationException(
                $"Cannot request cancel in status {Status}.");

        CancelRequested = true;
    }

    // --- Counter helpers (called by Hangfire executor) ---

    public void SetTotalInstances(int total) => TotalInstances = total;
    public void IncrementMigratedCount() => MigratedCount++;
    public void IncrementFailedCount() => FailedCount++;
    public void IncrementSkippedCount() => SkippedCount++;

    /// <summary>
    /// Recompute progress counts from items table (source of truth — not cumulative ++).
    /// Called after each batch commit.
    /// </summary>
    public void SetCounts(int migrated, int failed, int skipped)
    {
        MigratedCount = migrated;
        FailedCount = failed;
        SkippedCount = skipped;
    }

    private static string ComputeHash(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(bytes);
    }
}
