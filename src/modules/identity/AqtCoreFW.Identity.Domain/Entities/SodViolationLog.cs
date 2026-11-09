using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Audit log entry for a committed SoD violation override.
/// Created when an admin confirms assignment despite detected conflicts.
/// Tenant-scoped; reads/writes use inline filter (Identity module has NO global query filter).
///
/// T3 append-only plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index, used for outbox dedup
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
/// </summary>
public sealed class SodViolationLog : IMessageIdentified
{
    private SodViolationLog() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC timestamp when violation was detected and committed.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Tenant context — required for isolation (inline filter pattern).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>User who would receive the conflicting permission (null for role-only changes).</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Role whose permissions were being modified.</summary>
    public Guid? RoleId { get; private set; }

    /// <summary>
    /// FK to the SoD rule that was violated (long T1 PK — SodConflictRule is T1).
    /// ON DELETE RESTRICT — rules are soft-delete only; JOIN at read time returns
    /// current PermissionCodeA/B + Severity (no snapshot columns).
    /// Nullable kept defensive for future non-rule-linked violation types.
    /// AMEND-B2-Identity: changed from Guid? to long? to match SodConflictRule.Id (T1 long).
    /// </summary>
    public long? SodRuleId { get; private set; }

    /// <summary>Admin-provided justification (nullable, max 2000 chars).</summary>
    public string? Justification { get; private set; }

    /// <summary>Admin who performed the override action.</summary>
    public Guid ActorId { get; private set; }

    /// <summary>
    /// Factory — creates a new SoD violation log entry.
    /// MessageId is set via IGuidGenerator.NewIdMonotonic() (v7 — time-ordering benefit for FIFO dispatch).
    /// </summary>
    public static SodViolationLog Create(
        IGuidGenerator gen,
        Guid tenantId,
        Guid? userId,
        Guid? roleId,
        long? sodRuleId,
        string? justification,
        Guid actorId)
    {
        return new SodViolationLog
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            SodRuleId = sodRuleId,
            Justification = justification,
            ActorId = actorId,
        };
    }
}
