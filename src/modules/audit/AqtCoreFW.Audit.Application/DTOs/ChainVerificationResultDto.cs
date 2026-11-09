namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>Result of HMAC chain integrity verification for an audit log range.</summary>
public sealed record ChainVerificationResultDto(
    bool IsValid,
    long EntriesChecked,
    Guid? FirstTamperedEntryId,
    string? TamperDetail);
