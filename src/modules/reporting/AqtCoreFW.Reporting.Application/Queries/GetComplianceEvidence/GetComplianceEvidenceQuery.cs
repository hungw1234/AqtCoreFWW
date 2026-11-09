using AqtCoreFW.Reporting.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetComplianceEvidence;

/// <summary>Fetches audit data for compliance evidence PDF generation.</summary>
public record GetComplianceEvidenceQuery(
    string TenantId,
    string Framework,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo) : IRequest<Result<ComplianceEvidenceDto>>;
