using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: soft-deletes the caller's own signature profile (sets IsDeleted via the domain
/// <see cref="Domain.Entities.UserSignatureProfile.Delete"/>; the global query filter then hides it).
/// Resolved by (TenantId, CallerUserId, ProfilePublicId) → a profile owned by another user/tenant is a
/// <see cref="NotFoundError"/> (anti-IDOR). Deleting the default leaves the user with no default (benign).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteSignatureProfileCommand(
    Guid TenantId,
    Guid CallerUserId,
    Guid ProfilePublicId) : ICommand;

public sealed class DeleteSignatureProfileCommandValidator : AbstractValidator<DeleteSignatureProfileCommand>
{
    public DeleteSignatureProfileCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.ProfilePublicId).NotEmpty();
    }
}

public sealed class DeleteSignatureProfileCommandHandler(IUserSignatureProfileRepository repository)
    : IRequestHandler<DeleteSignatureProfileCommand, Result>
{
    public async Task<Result> Handle(DeleteSignatureProfileCommand cmd, CancellationToken ct)
    {
        var profile = await repository.GetByPublicIdAsync(cmd.TenantId, cmd.CallerUserId, cmd.ProfilePublicId, ct);
        if (profile is null)
            return Result.Fail(new NotFoundError($"Signature profile {cmd.ProfilePublicId} not found."));

        profile.Delete(); // soft-delete via ISoftDeletable; load was tracked so SaveChanges flushes
        await repository.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
