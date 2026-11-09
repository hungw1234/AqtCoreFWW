using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>
/// Read DTO for <c>AuditRetentionPolicy</c>.
///
/// Id is long (T1 bigint PK — by design, no PublicId AK). Phase-07 Group A retier
/// (2026-05-06) explicitly skipped this entity: admin-only governance entity with
/// no FE consumer + no external API consumer in repo. If external auditor consumer
/// later appears, revisit retier T1→T2.
///
/// RowVersion is base64-encoded for transport; clients must echo it back on
/// PUT/DELETE for optimistic concurrency.
/// </summary>
public sealed record AuditRetentionPolicyDto(
    long Id,
    Guid? TenantId,
    AuditCategory? Category,
    int RetentionDays,
    bool ArchiveBeforePurge,
    string? ArchivePath,
    string RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
