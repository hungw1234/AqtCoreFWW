using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Pollable status projection for a signing request. <see cref="Status"/> is computed at read time from the
/// underlying provider record (or the unlinked/creation-failure state) — never a stored, drift-prone column.
/// </summary>
public sealed record SigningRequestStatusDto(
    Guid PublicId,
    SigningRequestStatus Status,
    SigningArtifactKind ArtifactKind,
    string ProviderKey,
    string? FailureReason);
