using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: renames the caller's own signature profile. The profile is resolved by
/// (TenantId, CallerUserId, ProfilePublicId) so a profile owned by another user or tenant resolves to
/// null → <see cref="NotFoundError"/> (anti-IDOR; same 404 for "missing" and "not yours" avoids leaking
/// existence). Identity is JWT-injected by the controller, never body-bound.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateSignatureProfileCommand(
    Guid TenantId,
    Guid CallerUserId,
    Guid ProfilePublicId,
    string DisplayName,
    bool ShowSerial = true,
    bool ShowIssuer = true) : ICommand;

public sealed class UpdateSignatureProfileCommandValidator : AbstractValidator<UpdateSignatureProfileCommand>
{
    public UpdateSignatureProfileCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.ProfilePublicId).NotEmpty();
        // Reject whitespace-only (entity factory throws on it → keep it a 422, not a 500).
        RuleFor(x => x.DisplayName)
            .NotEmpty().MaximumLength(128)
            .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("DisplayName must not be whitespace-only.");
    }
}

public sealed class UpdateSignatureProfileCommandHandler(IUserSignatureProfileRepository repository)
    : IRequestHandler<UpdateSignatureProfileCommand, Result>
{
    public async Task<Result> Handle(UpdateSignatureProfileCommand cmd, CancellationToken ct)
    {
        var profile = await repository.GetByPublicIdAsync(cmd.TenantId, cmd.CallerUserId, cmd.ProfilePublicId, ct);
        if (profile is null)
            return Result.Fail(new NotFoundError($"Signature profile {cmd.ProfilePublicId} not found."));

        profile.Rename(cmd.DisplayName, cmd.CallerUserId);
        profile.SetStampVisibility(cmd.ShowSerial, cmd.ShowIssuer, cmd.CallerUserId);
        await repository.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
