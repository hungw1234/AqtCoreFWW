namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>
/// Marker for T2 (API-exposed aggregate) entities that carry a public-facing Guid alternate key.
/// Phase-2 Foundation Refactor (2026-05-03): T2 entities expose PublicId in API URLs and DTOs;
/// internal long Id stays unexposed. PublicId set by domain factory via IGuidGenerator (v7 default; v4 for sensitive entities like FormSubmission).
/// </summary>
public interface IPublicIdentified
{
    Guid PublicId { get; }
}
