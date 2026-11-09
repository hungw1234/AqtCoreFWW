using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: creates a signature profile owned by the calling user. <see cref="CallerUserId"/> and
/// <see cref="TenantId"/> are injected server-side from the JWT by the controller (never body-bound) so a
/// caller can only create profiles for themselves (anti-IDOR). New profiles are non-default; the user
/// promotes one explicitly via <see cref="SetDefaultSignatureProfileCommand"/>.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateSignatureProfileCommand(
    Guid TenantId,
    Guid CallerUserId,
    string DisplayName,
    bool ShowSerial = true,
    bool ShowIssuer = true) : ICommand<Guid>;

public sealed class CreateSignatureProfileCommandValidator : AbstractValidator<CreateSignatureProfileCommand>
{
    public CreateSignatureProfileCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        // Reject whitespace-only too — FluentValidation NotEmpty() accepts "   ", but the entity factory
        // throws on IsNullOrWhiteSpace → without this the bad input becomes a 500 instead of a clean 422.
        RuleFor(x => x.DisplayName)
            .NotEmpty().MaximumLength(128)
            .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("DisplayName must not be whitespace-only.");
    }
}

public sealed class CreateSignatureProfileCommandHandler(
    IUserSignatureProfileRepository repository,
    IGuidGenerator guidGen)
    : IRequestHandler<CreateSignatureProfileCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateSignatureProfileCommand cmd, CancellationToken ct)
    {
        // Owner = caller; createdBy = caller. The factory assigns a v4 PublicId returned to the client.
        var profile = UserSignatureProfile.Create(
            gen:         guidGen,
            tenantId:    cmd.TenantId,
            userId:      cmd.CallerUserId,
            displayName: cmd.DisplayName,
            createdBy:   cmd.CallerUserId,
            showSerial:  cmd.ShowSerial,
            showIssuer:  cmd.ShowIssuer);

        await repository.AddAsync(profile, ct);
        return Result.Ok(profile.PublicId);
    }
}
