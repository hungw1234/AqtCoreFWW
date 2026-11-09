using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Read model for a user-owned signature profile. Exposes the anti-IDOR <see cref="PublicId"/>
/// (never the internal long Id), the display name, the default flag, whether a signing
/// certificate enrollment has been linked yet (the cert link itself is resolved in later slices),
/// and per-slot flags telling the UI which visual signature images are already stored. Only the
/// presence of each image is exposed — never the storage key (the raw bytes are fetched through a
/// dedicated authorized path, not this list).
/// </summary>
public sealed record SignatureProfileDto(
    Guid PublicId,
    string DisplayName,
    bool IsDefault,
    bool HasCertLinked,
    bool HasFullImage,
    bool HasInitialImage,
    bool ShowSerial,
    bool ShowIssuer,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    /// <summary>Projects a domain profile to its read model — single mapping shared by the list + overview queries.</summary>
    public static SignatureProfileDto From(UserSignatureProfile p) => new(
        PublicId:        p.PublicId,
        DisplayName:     p.DisplayName,
        IsDefault:       p.IsDefault,
        HasCertLinked:   p.SignerCertEnrollmentId.HasValue,
        HasFullImage:    p.FullSignatureKey is not null,
        HasInitialImage: p.InitialSignatureKey is not null,
        ShowSerial:      p.ShowSerial,
        ShowIssuer:      p.ShowIssuer,
        CreatedAt:       p.CreatedAt,
        UpdatedAt:       p.UpdatedAt);
}
