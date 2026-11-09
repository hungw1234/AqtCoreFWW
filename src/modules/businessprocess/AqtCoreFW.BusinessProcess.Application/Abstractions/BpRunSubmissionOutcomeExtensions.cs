using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.BusinessProcess.Application.Abstractions;

/// <summary>
/// Translates the transport-neutral <see cref="BpRunSubmissionOutcome"/> (FluentResults-free
/// cross-module contract) into a FluentResults failure carrying the correct HTTP-mapping error
/// type: a cross-tenant rejection becomes a NotFoundError (404, does not leak existence); a form
/// failure becomes one ValidationError per field (422), preserving FieldKey for FE rendering.
/// Centralised so all three BP write handlers map outcomes identically.
/// </summary>
internal static class BpRunSubmissionOutcomeExtensions
{
    public static Result<T> ToFailureResult<T>(
        this BpRunSubmissionOutcome outcome, string crossTenantNotFoundMessage)
    {
        if (outcome.ErrorCode == BusinessProcessErrorCodes.CrossTenantRejected)
            return Result.Fail<T>(new NotFoundError(
                $"{BusinessProcessErrorCodes.CrossTenantRejected}: {crossTenantNotFoundMessage}"));

        if (outcome.FieldErrors is { Count: > 0 })
            return Result.Fail<T>(
                outcome.FieldErrors.Select(f => (IError)new ValidationError(f.Message, f.Field)));

        return Result.Fail<T>(new ValidationError("BP-Run submission failed."));
    }
}
