using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service read-back of one visual signature image slot so the profile UI can preview an already-stored
/// image (the overview DTO only carries presence booleans, not bytes). Scoped to (TenantId, CallerUserId)
/// injected from the JWT — a caller can only read their own profile's image (anti-IDOR); the slot is resolved
/// by <see cref="ProfilePublicId"/> + <see cref="VisualType"/>. A profile that is not theirs, or an empty
/// slot, returns a <c>NotFoundError</c> (→404) rather than leaking existence.
/// </summary>
public sealed record GetMySignatureImageQuery(
    Guid TenantId,
    Guid CallerUserId,
    Guid ProfilePublicId,
    SignatureVisualType VisualType) : IQuery<SignatureImageContent>;

/// <summary>Raw bytes of a stored signature image plus the content-type derived from its magic bytes.</summary>
public sealed record SignatureImageContent(byte[] Content, string ContentType);

public sealed class GetMySignatureImageQueryHandler(
    IUserSignatureProfileRepository repository,
    IUserSignatureImageStore imageStore)
    : IRequestHandler<GetMySignatureImageQuery, Result<SignatureImageContent>>
{
    public async Task<Result<SignatureImageContent>> Handle(GetMySignatureImageQuery query, CancellationToken ct)
    {
        // Scope the lookup to the caller's (tenant, user) — a profile owned by anyone else returns null → 404,
        // never a cross-user/tenant read (same scoping the upload handler and P1 integration tests rely on).
        var profile = await repository.GetByPublicIdAsync(
            query.TenantId, query.CallerUserId, query.ProfilePublicId, ct);
        if (profile is null)
            return Result.Fail(new NotFoundError($"Signature profile {query.ProfilePublicId} not found."));

        var storageKey = query.VisualType == SignatureVisualType.FullSignature
            ? profile.FullSignatureKey
            : profile.InitialSignatureKey;

        // Empty slot → 404 (the slot simply has no image yet); do not distinguish from a missing profile.
        if (string.IsNullOrEmpty(storageKey))
            return Result.Fail(new NotFoundError($"Signature image slot {query.VisualType} is empty."));

        // GetAsync throws when the object is absent — a dangling key (DB points at a reclaimed blob) is a
        // storage inconsistency, not a client error, so let it surface as a 500 rather than masking it as 404.
        var bytes = await imageStore.GetAsync(storageKey, ct);

        return Result.Ok(new SignatureImageContent(bytes, DetectImageContentType(bytes)));
    }

    // The store never persists the content-type (upload narrows to PNG/JPEG by magic bytes and discards the
    // declared type — S8). Re-derive it from the same magic bytes so the response Content-Type is correct;
    // default to PNG for any unexpected shape (only PNG/JPEG can have been stored).
    private static string DetectImageContentType(byte[] bytes)
    {
        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        // PNG: 89 50 4E 47
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        return "image/png";
    }
}
