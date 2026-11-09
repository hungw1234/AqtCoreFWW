using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.DownloadSubmissionSignedStep;

/// <summary>
/// Streams the signed PDF for a specific signing step of a form submission.
///
/// Security invariants:
///  1. Submission tenant guard first — cross-tenant publicId returns 404.
///  2. Null SigningRequestId (no signing initiated) → 404.
///  3. Storage key resolved server-side via ISigningRequestClient — client never supplies bucket/key.
///  4. Only Completed steps have a signed PDF; not-Completed → 404.
/// </summary>
public sealed class DownloadSubmissionSignedStepQueryHandler(
    IReadDbConnection db,
    ISigningRequestClient signingClient,
    ISignedDocumentDownloader downloader)
    : IRequestHandler<DownloadSubmissionSignedStepQuery, Result<DownloadSubmissionSignedStepResult>>
{
    public async Task<Result<DownloadSubmissionSignedStepResult>> Handle(
        DownloadSubmissionSignedStepQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1: load submission signing link with tenant guard (cross-tenant → 404).
        // TenantId predicate explicit (db_owner bypasses RLS in the integration test context).
        const string sql = """
            SELECT SigningRequestId
            FROM forms.FormSubmissions
            WHERE PublicId = @SubmissionPublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<SubmissionSigningRow>(
            sql,
            new { request.SubmissionPublicId, request.TenantId },
            cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"FormSubmission {request.SubmissionPublicId} not found."));

        if (row.SigningRequestId is null)
            return Result.Fail(new NotFoundError(
                $"FormSubmission {request.SubmissionPublicId} has no signing request."));

        // Step 2: resolve storage key server-side (tenant-scoped inside Identity impl).
        var resolution = await signingClient.ResolveSignedStepForRequestAsync(
            request.TenantId, row.SigningRequestId.Value, request.SequenceNumber, cancellationToken);

        if (!resolution.Found)
            return Result.Fail(new NotFoundError(
                $"No completed signed PDF found for submission {request.SubmissionPublicId} " +
                $"sequence {request.SequenceNumber}. Reason: {resolution.ErrorCode}"));

        // Step 3: stream from signing-docflow bucket. Key is server-minted; never caller-supplied.
        Stream stream;
        try
        {
            stream = await downloader.DownloadAsync(
                resolution.Bucket!, resolution.StorageKey!, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Result.Fail(new NotFoundError(
                $"Signed PDF for submission {request.SubmissionPublicId} " +
                $"sequence {request.SequenceNumber} is no longer in storage."));
        }

        return Result.Ok(new DownloadSubmissionSignedStepResult(
            stream,
            resolution.FileName ?? $"signed-submission-{request.SubmissionPublicId}-seq{request.SequenceNumber}.pdf",
            "application/pdf"));
    }

    private sealed class SubmissionSigningRow
    {
        public Guid? SigningRequestId { get; init; }
    }
}
