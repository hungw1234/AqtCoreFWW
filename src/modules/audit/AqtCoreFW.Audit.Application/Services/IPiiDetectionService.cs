namespace AqtCoreFW.Audit.Application.Services;

/// <summary>PII detection service — scans text for Vietnamese and international PII patterns.</summary>
public interface IPiiDetectionService
{
    /// <summary>Scans text and returns all detected PII items with position info.</summary>
    Task<IReadOnlyList<PiiDetection>> DetectAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>A single PII match found in the scanned text.</summary>
public sealed record PiiDetection(PiiType Type, int StartIndex, int Length);

public enum PiiType
{
    /// <summary>Vietnamese national ID (CCCD) — 12 digits.</summary>
    NationalId,
    /// <summary>Vietnamese phone number — 10 digits starting with 0.</summary>
    PhoneNumber,
    Email,
    /// <summary>Potential Vietnamese personal name — capitalised word sequence.</summary>
    VietnameseName
}
