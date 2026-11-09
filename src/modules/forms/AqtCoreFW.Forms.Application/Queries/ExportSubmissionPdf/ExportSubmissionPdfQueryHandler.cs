using AqtCoreFW.Forms.Application.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.ExportSubmissionPdf;

/// <summary>
/// Exports a form submission as a QuestPDF-generated PDF byte array.
/// Data loading + PDF generation delegated to <see cref="ISubmissionPdfComposer"/>.
/// Table-mode submissions (SubmissionData IS NULL) show a notice instead of field rows.
/// SubmissionId = FormSubmission.PublicId (Guid, T2 external AK).
/// </summary>
public sealed class ExportSubmissionPdfQueryHandler(ISubmissionPdfComposer composer)
    : IRequestHandler<ExportSubmissionPdfQuery, Result<byte[]>>
{
    public async Task<Result<byte[]>> Handle(
        ExportSubmissionPdfQuery request,
        CancellationToken cancellationToken)
    {
        var result = await composer.ComposeAsync(request.SubmissionId, request.TenantId, cancellationToken);
        if (result.IsFailed)
            return result.ToResult();

        return Result.Ok(result.Value.Bytes);
    }
}
