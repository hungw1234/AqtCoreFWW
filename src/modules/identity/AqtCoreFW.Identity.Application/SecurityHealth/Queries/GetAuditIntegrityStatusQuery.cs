using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.SecurityHealth.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// QĐ 742 §3.4.a — D7 visibility: audit log immutability / integrity status.
/// Checks whether the SQL Server trigger <c>tr_AuditLog_NoMutate</c> is active
/// (blocks UPDATE/DELETE on [audit].AuditLogEntries) and reports total entry count.
/// </summary>
public sealed record GetAuditIntegrityStatusQuery : IRequest<Result<AuditIntegrityStatusDto>>;

public sealed class GetAuditIntegrityStatusQueryValidator
    : AbstractValidator<GetAuditIntegrityStatusQuery>
{
    public GetAuditIntegrityStatusQueryValidator() { /* no parameters */ }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Integrity snapshot returned by <see cref="GetAuditIntegrityStatusQuery"/>.
/// ImmutabilityTriggerActive = true means the DB-level guard is in place.
/// </summary>
public sealed record AuditIntegrityStatusDto(
    /// <summary>
    /// True when tr_AuditLog_NoMutate exists and is NOT disabled in sys.triggers.
    /// False means the guard is missing or disabled — a compliance gap (QĐ 742 §3.4.a).
    /// </summary>
    bool ImmutabilityTriggerActive,
    string TriggerName,
    long TotalLogEntries);

// ── Handler ──────────────────────────────────────────────────────────────────

// Reads [audit] through the audit module's cross-module client (ISO read-isolation): identity holds
// no [audit]-capable DB credential — the audit-owner login + audit-admin bypass live in the audit module.
public sealed class GetAuditIntegrityStatusQueryHandler(IAuditEvidenceClient auditEvidence)
    : IRequestHandler<GetAuditIntegrityStatusQuery, Result<AuditIntegrityStatusDto>>
{
    public async Task<Result<AuditIntegrityStatusDto>> Handle(
        GetAuditIntegrityStatusQuery request, CancellationToken ct)
    {
        var status = await auditEvidence.GetIntegrityStatusAsync(ct);

        return Result.Ok(new AuditIntegrityStatusDto(
            ImmutabilityTriggerActive: status.ImmutabilityTriggerActive,
            TriggerName:               status.TriggerName,
            TotalLogEntries:           status.TotalLogEntries));
    }
}
