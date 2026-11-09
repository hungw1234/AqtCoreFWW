using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.SecurityHealth.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// QĐ 742 §3.2.a/3.2.b — D7 visibility: audit retention policies + live row counts.
/// Returns configured retention windows per (TenantId, Category) pair
/// and the live row count in the hot AuditLogEntries table.
/// No archive table exists in this deployment — ArchiveRowCount is always null.
/// </summary>
public sealed record GetAuditRetentionStatusQuery(Guid? TenantId)
    : IRequest<Result<AuditRetentionStatusDto>>;

public sealed class GetAuditRetentionStatusQueryValidator
    : AbstractValidator<GetAuditRetentionStatusQuery>
{
    public GetAuditRetentionStatusQueryValidator() { /* TenantId nullable OK — null = global */ }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Retention window for one (TenantId, Category) pair.</summary>
public sealed record RetentionPolicyLineDto(
    long    Id,
    Guid?   TenantId,
    /// <summary>Null means "all categories" (the global-default row).</summary>
    int?    CategoryId,
    int     RetentionDays,
    bool    ArchiveBeforePurge,
    string? ArchivePath);

/// <summary>
/// Aggregate retention status: configured policies + current live row count.
/// ArchiveRowCount is null because no archive table is provisioned.
/// </summary>
public sealed record AuditRetentionStatusDto(
    IReadOnlyList<RetentionPolicyLineDto> Policies,
    long HotRowCount,
    /// <summary>
    /// Reserved: null until an archive sink (cold storage / secondary table) is provisioned.
    /// </summary>
    long? ArchiveRowCount);

// ── Handler ──────────────────────────────────────────────────────────────────

// Reads [audit] through the audit module's cross-module client (ISO read-isolation): identity holds
// no [audit]-capable DB credential — the audit-owner login + audit-admin bypass live in the audit module.
public sealed class GetAuditRetentionStatusQueryHandler(IAuditEvidenceClient auditEvidence)
    : IRequestHandler<GetAuditRetentionStatusQuery, Result<AuditRetentionStatusDto>>
{
    public async Task<Result<AuditRetentionStatusDto>> Handle(
        GetAuditRetentionStatusQuery request, CancellationToken ct)
    {
        var status = await auditEvidence.GetRetentionPolicyStatusAsync(request.TenantId, ct);

        var policies = status.Policies
            .Select(p => new RetentionPolicyLineDto(
                p.Id, p.TenantId, p.CategoryId,
                p.RetentionDays, p.ArchiveBeforePurge, p.ArchivePath))
            .ToList();

        return Result.Ok(new AuditRetentionStatusDto(
            policies,
            status.HotRowCount,
            ArchiveRowCount: status.ArchiveRowCount));
    }
}
