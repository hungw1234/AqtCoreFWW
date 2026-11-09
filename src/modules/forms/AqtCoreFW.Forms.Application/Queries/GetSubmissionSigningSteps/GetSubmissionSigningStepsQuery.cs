using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Forms.Application.Queries.GetSubmissionSigningSteps;

/// <summary>
/// Returns the ordered list of signing steps for a form submission.
/// Submission tenant guard runs first (cross-tenant publicId → 404).
/// Null SigningRequestId (no signing initiated) → empty list (not an error).
/// </summary>
public sealed record GetSubmissionSigningStepsQuery(
    Guid SubmissionPublicId,
    Guid TenantId)
    : IQuery<IReadOnlyList<SigningStepDto>>;
