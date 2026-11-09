using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetSubmissionSigningSteps;

/// <summary>
/// Lists signing steps for a form submission's signed PDF.
///
/// Tenant guard: loads submission via Dapper with TenantId predicate (cross-tenant → 404,
/// matching the pattern in GetFormSubmissionQueryHandler). Then delegates to
/// <see cref="ISigningRequestClient.GetStepsForRequestPublicIdAsync"/> using the stored
/// <c>FormSubmission.SigningRequestId</c> (= SigningRequest.PublicId). Null SigningRequestId
/// (no signing initiated yet) returns an empty list — not a 404.
/// </summary>
public sealed class GetSubmissionSigningStepsQueryHandler(
    IReadDbConnection db,
    ISigningRequestClient signingClient)
    : IRequestHandler<GetSubmissionSigningStepsQuery, Result<IReadOnlyList<SigningStepDto>>>
{
    public async Task<Result<IReadOnlyList<SigningStepDto>>> Handle(
        GetSubmissionSigningStepsQuery request,
        CancellationToken cancellationToken)
    {
        // Load only the signing link field — submission existence + tenant guard in one query.
        // TenantId predicate is explicit (db_owner bypasses RLS in the integration test context).
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

        // Null SigningRequestId = no signing initiated — return empty list, not an error.
        if (row.SigningRequestId is null)
            return Result.Ok<IReadOnlyList<SigningStepDto>>(Array.Empty<SigningStepDto>());

        var steps = await signingClient.GetStepsForRequestPublicIdAsync(
            request.TenantId, row.SigningRequestId.Value, cancellationToken);

        return Result.Ok(steps);
    }

    private sealed class SubmissionSigningRow
    {
        public Guid? SigningRequestId { get; init; }
    }
}
