namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// SQL LIKE pattern escaping for Dapper queries in Application layer handlers.
/// MUST use before interpolating user input into LIKE patterns to prevent LIKE injection.
/// Usage: var safe = userInput.EscapeSqlLike(); then use LIKE @Param ESCAPE '\'.
/// </summary>
public static class EscapeSqlLikeExtensions
{
    /// <summary>Escapes %, _, and [ for safe SQL LIKE patterns.</summary>
    public static string EscapeSqlLike(this string value, char escapeChar = '\\') =>
        value
            .Replace(escapeChar.ToString(), $"{escapeChar}{escapeChar}")
            .Replace("%", $"{escapeChar}%")
            .Replace("_", $"{escapeChar}_")
            .Replace("[", $"{escapeChar}[");
}
