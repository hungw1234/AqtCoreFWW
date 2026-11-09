using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: uploads a visual signature image (PNG/JPEG) into one slot of a profile the caller owns.
/// <see cref="CallerUserId"/> and <see cref="TenantId"/> are JWT-injected by the controller (never
/// body-bound) so a caller can only write to their own profile (anti-IDOR); the profile is resolved by
/// <see cref="ProfilePublicId"/> scoped to that (tenant, user). The bytes are validated and stored through
/// <see cref="IUserSignatureImageStore"/> (Files security pipeline → PNG/JPEG narrowing, content-type
/// ignored — S8); the returned storage key replaces only the targeted slot, preserving the other image.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UploadSignatureImageCommand(
    Guid TenantId,
    Guid CallerUserId,
    Guid ProfilePublicId,
    SignatureVisualType VisualType,
    byte[] Content,
    string FileName) : ICommand<Guid>;

public sealed class UploadSignatureImageCommandValidator : AbstractValidator<UploadSignatureImageCommand>
{
    /// <summary>Max raster signature image size — a stamp glyph never needs more (anti-DoS upload cap).</summary>
    public const int MaxImageBytes = 500 * 1024;

    public UploadSignatureImageCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.ProfilePublicId).NotEmpty();
        RuleFor(x => x.VisualType).IsInEnum();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Image content must not be empty.")
            .Must(c => c is { Length: <= MaxImageBytes })
            .WithMessage($"Image must not exceed {MaxImageBytes / 1024} KB.");
    }
}

public sealed class UploadSignatureImageCommandHandler(
    IUserSignatureProfileRepository repository,
    IUserSignatureImageStore imageStore)
    : IRequestHandler<UploadSignatureImageCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(UploadSignatureImageCommand cmd, CancellationToken ct)
    {
        // Scope the lookup to the caller's (tenant, user) — a profile owned by anyone else returns null → 404,
        // never a cross-user/tenant write (the repo applies the same scoping proven in P1 integration tests).
        var profile = await repository.GetByPublicIdAsync(cmd.TenantId, cmd.CallerUserId, cmd.ProfilePublicId, ct);
        if (profile is null)
            return Result.Fail(new NotFoundError($"Signature profile {cmd.ProfilePublicId} not found."));

        // Validate + store the bytes through the host-bound Files pipeline. A rejected image (SVG, polyglot,
        // non-PNG/JPEG) comes back as a ValidationError result — propagate it as-is (→422), do not persist.
        var stored = await imageStore.ValidateAndStoreAsync(
            cmd.TenantId, cmd.CallerUserId, cmd.Content, cmd.FileName, ct);
        if (stored.IsFailed)
            return stored.ToResult();

        var storageKey = stored.Value;

        // Capture the slot's current key BEFORE overwriting so its now-orphaned blob can be reclaimed.
        var priorKey = cmd.VisualType == SignatureVisualType.FullSignature
            ? profile.FullSignatureKey
            : profile.InitialSignatureKey;

        // Write only the targeted slot; preserve the other image key (SetImages replaces both).
        var (fullKey, initialKey) = cmd.VisualType == SignatureVisualType.FullSignature
            ? (storageKey, profile.InitialSignatureKey)
            : (profile.FullSignatureKey, storageKey);
        profile.SetImages(fullKey, initialKey, cmd.CallerUserId);

        await repository.SaveChangesAsync(ct);

        // Reclaim the replaced blob only AFTER the DB points at the new key (so a save failure never deletes a
        // still-referenced image). Best-effort — DeleteAsync swallows errors (M2: prevents orphan accumulation).
        if (priorKey is not null && priorKey != storageKey)
            await imageStore.DeleteAsync(priorKey, ct);

        return Result.Ok(profile.PublicId);
    }
}
