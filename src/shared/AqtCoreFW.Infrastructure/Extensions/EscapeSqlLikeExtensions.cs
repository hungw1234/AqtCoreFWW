namespace AqtCoreFW.Infrastructure.Extensions;

/// <summary>
/// SQL LIKE pattern escaping for Dapper queries.
/// MUST use before interpolating user input into LIKE patterns.
/// Direct string interpolation in QueryAsync is prohibited (GOV_DPR_001 SonarQube rule).
/// Usage: var safe = userInput.EscapeSqlLike(); then pass via @param DynamicParameters.
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
