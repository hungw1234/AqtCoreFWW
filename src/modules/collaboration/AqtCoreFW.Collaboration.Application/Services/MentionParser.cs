using System.Text.RegularExpressions;

namespace AqtCoreFW.Collaboration.Application.Services;

/// <summary>
/// Extracts @mention Guids from message content.
/// Format: @xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx (standard Guid, lowercase hex).
/// </summary>
public static partial class MentionParser
{
    // Matches @{Guid} — standard 8-4-4-4-12 hex format
    [GeneratedRegex(@"@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    /// <summary>Returns all unique user Guids mentioned in the content string.</summary>
    public static IReadOnlyList<Guid> ExtractMentions(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        return MentionRegex()
            .Matches(content)
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();
    }
}
