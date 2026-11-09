using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Application.Queries.GetComplianceEvidence;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.GenerateCompliancePdf;

/// <summary>
/// Fetches compliance evidence data then delegates PDF generation to ICompliancePdfGenerator.
/// Keeps Application layer free of QuestPDF dependency — generator is in Infrastructure.
/// </summary>
public sealed class GenerateCompliancePdfCommandHandler(
    IMediator mediator,
    ICompliancePdfGenerator pdfGenerator)
    : IRequestHandler<GenerateCompliancePdfCommand, Result<byte[]>>
{
    public async Task<Result<byte[]>> Handle(
        GenerateCompliancePdfCommand request,
        CancellationToken cancellationToken)
    {
        var evidenceResult = await mediator.Send(
            new GetComplianceEvidenceQuery(
                request.TenantId,
                request.Framework,
                request.PeriodFrom,
                request.PeriodTo),
            cancellationToken);

        if (evidenceResult.IsFailed)
            return evidenceResult.ToResult<byte[]>();

        // Inject actual generated-by name into the DTO
        var evidence = evidenceResult.Value with { GeneratedBy = request.GeneratedBy };

        var pdfBytes = await pdfGenerator.GenerateAsync(evidence, cancellationToken);
        return Result.Ok(pdfBytes);
    }
}
