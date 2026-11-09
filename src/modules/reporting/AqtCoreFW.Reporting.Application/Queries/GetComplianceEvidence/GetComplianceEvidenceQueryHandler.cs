using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetComplianceEvidence;

/// <summary>
/// Aggregates audit, security incident, access review, and retention data for compliance PDF.
/// Audit + retention evidence delegated to <see cref="IAuditEvidenceClient"/> (Audit-owned)
/// — replaces the previous cross-schema FROM audit.* Dapper queries (v2.132p10 refactor).
/// Access-review aggregation delegates to <see cref="IIdentityAuditClient"/> (Identity-owned).
/// </summary>
public sealed class GetComplianceEvidenceQueryHandler(
    IAuditEvidenceClient auditEvidence,
    IIdentityAuditClient identityAudit)
    : IRequestHandler<GetComplianceEvidenceQuery, Result<ComplianceEvidenceDto>>
{
    public async Task<Result<ComplianceEvidenceDto>> Handle(
        GetComplianceEvidenceQuery request,
        CancellationToken cancellationToken)
    {
        // request.TenantId is a string from the HTTP layer; both clients want strongly typed Guid.
        var tenantGuid = Guid.Parse(request.TenantId);

        var evidence = await auditEvidence.GetComplianceEvidenceAsync(
            tenantGuid, request.PeriodFrom, request.PeriodTo, cancellationToken);

        var auditSummary = new AuditSummaryDto(
            TotalEvents: evidence.AuditSummary.TotalEvents,
            LoginEvents: evidence.AuditSummary.LoginEvents,
            DataAccessEvents: evidence.AuditSummary.DataAccessEvents,
            AdminEvents: evidence.AuditSummary.AdminEvents,
            PeriodFrom: evidence.AuditSummary.PeriodFrom,
            PeriodTo: evidence.AuditSummary.PeriodTo);

        var incidents = evidence.SecurityIncidents
            .Select(i => new SecurityIncidentSummaryDto(
                IncidentId: i.IncidentId,
                Severity: i.Severity,
                Status: i.Status,
                Description: i.Description,
                OccurredAt: i.OccurredAt))
            .ToList();

        var retention = new DataRetentionStatusDto(
            TotalRecords: evidence.RetentionStatus.TotalRecords,
            ArchivedRecords: evidence.RetentionStatus.ArchivedRecords,
            PurgedRecords: evidence.RetentionStatus.PurgedRecords,
            PendingPurge: evidence.RetentionStatus.PendingPurge);

        var accessReviewSummary = await identityAudit.GetAccessReviewStatusAsync(
            tenantGuid, request.PeriodFrom, request.PeriodTo, cancellationToken);

        var accessReview = new AccessReviewStatusDto(
            TotalReviewed: accessReviewSummary.TotalReviewed,
            Approved: accessReviewSummary.Approved,
            Revoked: accessReviewSummary.Revoked,
            LastReviewDate: accessReviewSummary.LastReviewDate);

        var dto = new ComplianceEvidenceDto(
            Framework: request.Framework,
            GeneratedAt: DateTimeOffset.UtcNow,
            GeneratedBy: "System",
            AuditSummary: auditSummary,
            SecurityIncidents: incidents,
            AccessReviewStatus: accessReview,
            DataRetentionStatus: retention);

        return Result.Ok(dto);
    }
}
