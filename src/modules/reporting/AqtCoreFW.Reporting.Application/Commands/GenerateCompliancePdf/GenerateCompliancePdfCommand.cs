using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.GenerateCompliancePdf;

/// <summary>
/// Generates a compliance evidence PDF for the specified framework and period.
/// Returns raw PDF bytes for streaming to the client.
/// </summary>
public record GenerateCompliancePdfCommand(
    string TenantId,
    string Framework,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    string GeneratedBy) : IRequest<Result<byte[]>>;
