namespace AqtCoreFW.Reporting.Application.DTOs;

/// <summary>Data bundle passed to ICompliancePdfGenerator for GOV compliance PDF generation.</summary>
public record ComplianceEvidenceDto(
    string Framework,
    DateTimeOffset GeneratedAt,
    string GeneratedBy,
    AuditSummaryDto AuditSummary,
    IReadOnlyList<SecurityIncidentSummaryDto> SecurityIncidents,
    AccessReviewStatusDto AccessReviewStatus,
    DataRetentionStatusDto DataRetentionStatus);

public record AuditSummaryDto(
    int TotalEvents,
    int LoginEvents,
    int DataAccessEvents,
    int AdminEvents,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo);

public record SecurityIncidentSummaryDto(
    string IncidentId,
    string Severity,
    string Status,
    string Description,
    DateTimeOffset OccurredAt);

public record AccessReviewStatusDto(
    int TotalReviewed,
    int Approved,
    int Revoked,
    DateTimeOffset LastReviewDate);

public record DataRetentionStatusDto(
    int TotalRecords,
    int ArchivedRecords,
    int PurgedRecords,
    int PendingPurge);
