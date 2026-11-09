using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.DTOs;

/// <summary>
/// Centralised JSON parsing for DecisionTable raw storage strings (InputColumns / OutputColumns / Rows).
/// Read-path utility — malformed JSON is swallowed and reported via the optional logger; never throws.
/// Domain default for these fields is "[]" so corruption indicates upstream tampering, not normal flow.
/// </summary>
public static class DecisionTableJsonHelper
{
    /// <summary>Deserialize a JSON array of strings (e.g. "[\"a\",\"b\"]") into a list. Empty list on null/blank/malformed.</summary>
    public static IReadOnlyList<string> ParseColumns(string? json, ILogger? logger = null, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Malformed DecisionTable columns JSON ignored. Context={Context}", context);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Deserialize a JSON array of objects (decision rows) into a list of dictionaries with native CLR values.
    /// JsonElement values are flattened so System.Text.Json can re-serialize them on the wire without leaking.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParseRows(
        string? json,
        ILogger? logger = null,
        string? context = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<IReadOnlyDictionary<string, object?>>();

        try
        {
            var raw = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            if (raw is null)
                return Array.Empty<IReadOnlyDictionary<string, object?>>();

            var converted = new List<IReadOnlyDictionary<string, object?>>(raw.Count);
            foreach (var row in raw)
            {
                var dict = new Dictionary<string, object?>(row.Count);
                foreach (var kv in row)
                    dict[kv.Key] = ToNetValue(kv.Value);
                converted.Add(dict);
            }
            return converted;
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Malformed DecisionTable rows JSON ignored. Context={Context}", context);
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }
    }

    // Recursively flatten JsonElement → native CLR types so the DTO doesn't carry a serializer-specific type
    // through the Application layer. Keeps onion clean while preserving wire fidelity.
    private static object? ToNetValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(ToNetValue).ToList(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => ToNetValue(p.Value)),
        _ => null
    };
}
