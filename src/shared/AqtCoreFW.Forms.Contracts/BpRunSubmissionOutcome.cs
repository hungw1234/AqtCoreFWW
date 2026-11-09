namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// One field-level validation error from a BP-Run form submission. Plain record so the
/// cross-module contract stays transport-neutral (gRPC/JSON safe) per the FluentResults-free
/// design of <see cref="IFormSubmissionRunClient"/>. Field carries the FormField key so the FE
/// can render the error against the right input; empty when the failure is not field-scoped.
/// </summary>
public sealed record BpRunFieldError(string Field, string Message);

/// <summary>
/// Outcome of <see cref="IFormSubmissionRunClient.CreateForBpRunAsync"/>. Plain record (no
/// FluentResults) so the cross-module boundary stays transport-neutral. The BP module translates
/// this into FluentResults at its Application boundary: a cross-tenant rejection maps to a 404 that
/// does not leak existence; field failures map to 422 validation errors that preserve FieldKey.
///
/// Expected outcomes never throw — only genuinely unexpected faults (DB, infra) surface as exceptions.
/// </summary>
public sealed record BpRunSubmissionOutcome
{
    /// <summary>Set only on success — the new submission's PublicId (Guid T2 AK).</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>GOV error code for a non-field rejection (currently only GOV_BP_002 cross-tenant).</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Field-level validation errors when the inner form submission failed validation.</summary>
    public IReadOnlyList<BpRunFieldError>? FieldErrors { get; init; }

    public bool IsSuccess => SubmissionId.HasValue;

    public static BpRunSubmissionOutcome Success(Guid submissionId)
        => new() { SubmissionId = submissionId };

    public static BpRunSubmissionOutcome CrossTenant(string errorCode)
        => new() { ErrorCode = errorCode };

    public static BpRunSubmissionOutcome SubmissionFailed(IReadOnlyList<BpRunFieldError> fieldErrors)
        => new() { FieldErrors = fieldErrors };
}
