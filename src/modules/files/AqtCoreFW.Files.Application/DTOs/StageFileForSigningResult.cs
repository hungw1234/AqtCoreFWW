namespace AqtCoreFW.Files.Application.DTOs;

/// <summary>
/// Returned by StageFileForSigningCommand: the minted document id and the
/// tenant-prefixed storage key in the signing-original bucket.
/// The signing orchestrator uses these to initiate a DocFlow USB round-trip.
/// </summary>
public sealed record StageFileForSigningResult(Guid DocId, string OriginalStorageKey);
