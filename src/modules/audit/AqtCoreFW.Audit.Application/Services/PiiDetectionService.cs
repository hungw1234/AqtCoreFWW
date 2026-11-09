using System.Text.RegularExpressions;

namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Regex-based PII scanner for Vietnamese and international PII patterns.
/// Patterns cover: CCCD (12 digits), Vietnamese phone (10 digits starting 0),
/// email addresses, and potential Vietnamese name sequences.
/// Returns list of PiiDetection items — callers decide whether to redact or block.
/// </summary>
public sealed class PiiDetectionService : IPiiDetectionService
{
    // Vietnamese CCCD: exactly 12 consecutive digits (not part of a longer number)
    private static readonly Regex CccdPattern =
        new(@"(?<!\d)\d{12}(?!\d)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // Vietnamese mobile: 0 followed by 9 more digits (e.g. 0912345678)
    private static readonly Regex PhonePattern =
        new(@"(?<!\d)0\d{9}(?!\d)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // Standard email
    private static readonly Regex EmailPattern =
        new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // Vietnamese name heuristic: 2–4 consecutive Title-case words using Vietnamese alphabet
    // Covers common diacritics used in Vietnamese names
    private static readonly Regex VietNamePattern =
        new(@"\b[A-ZÁÀẢÃẠĂẮẰẲẴẶÂẤẦẨẪẬĐÉÈẺẼẸÊẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌÔỐỒỔỖỘƠỚỜỞỠỢÚÙỦŨỤƯỨỪỬỮỰÝỲỶỸỴ][a-záàảãạăắằẳẵặâấầẩẫậđéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵ]+" +
             @"(?:\s+[A-ZÁÀẢÃẠĂẮẰẲẴẶÂẤẦẨẪẬĐÉÈẺẼẸÊẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌÔỐỒỔỖỘƠỚỜỞỠỢÚÙỦŨỤƯỨỪỬỮỰÝỲỶỸỴ][a-záàảãạăắằẳẵặâấầẩẫậđéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵ]+){1,3}\b",
             RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PiiDetection>();

        if (string.IsNullOrEmpty(text))
            return Task.FromResult<IReadOnlyList<PiiDetection>>(results);

        AddMatches(results, CccdPattern, text, PiiType.NationalId);
        AddMatches(results, PhonePattern, text, PiiType.PhoneNumber);
        AddMatches(results, EmailPattern, text, PiiType.Email);
        AddMatches(results, VietNamePattern, text, PiiType.VietnameseName);

        return Task.FromResult<IReadOnlyList<PiiDetection>>(results);
    }

    private static void AddMatches(List<PiiDetection> results, Regex regex, string text, PiiType type)
    {
        foreach (Match m in regex.Matches(text))
            results.Add(new PiiDetection(type, m.Index, m.Length));
    }
}
