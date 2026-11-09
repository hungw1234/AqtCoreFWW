using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.MfaEmergency;

/// <summary>
/// Submits a request to disable MFA for a target user.
/// Requires a second admin to approve (ApproveEmergencyDisableCommand) before MFA is cleared.
/// </summary>
public sealed record RequestEmergencyDisableCommand(
    Guid TargetUserId,
    string Reason) : IRequest<Result<long>>;

public sealed class RequestEmergencyDisableCommandValidator
    : AbstractValidator<RequestEmergencyDisableCommand>
{
    public RequestEmergencyDisableCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RequestEmergencyDisableCommandHandler(
    IMfaEmergencyDisableRequestRepository repository,
    UserManager<ApplicationUser> userManager,
    ICurrentUser currentUser)
    : IRequestHandler<RequestEmergencyDisableCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        RequestEmergencyDisableCommand request, CancellationToken ct)
    {
        // Target user must exist and have MFA enrolled.
        var targetUser = await userManager.FindByIdAsync(request.TargetUserId.ToString());
        if (targetUser is null)
            return Result.Fail(new NotFoundError($"User {request.TargetUserId} not found."));

        if (targetUser.TotpEnrolledAt is null)
            return Result.Fail(new BadRequestError(
                "Target user does not have MFA enrolled.", ErrorCodes.Identity.MfaRequired));

        // Block duplicate pending requests for the same target user.
        var existing = await repository.GetPendingForUserAsync(request.TargetUserId, ct);
        if (existing is not null)
            return Result.Fail(new ConflictError(
                $"A pending emergency disable request already exists for user {request.TargetUserId}."));

        var disableRequest = MfaEmergencyDisableRequest.Create(
            request.TargetUserId,
            currentUser.UserId,
            request.Reason);

        await repository.AddAsync(disableRequest, ct);
        // T1: return long Id (DB-assigned IDENTITY after SaveChanges in AddAsync)
        return Result.Ok(disableRequest.Id);
    }
}
