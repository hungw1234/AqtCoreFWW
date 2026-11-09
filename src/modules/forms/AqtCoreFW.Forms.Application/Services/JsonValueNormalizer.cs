using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Boundary normalizer for `Dictionary&lt;string, object?&gt;` values produced by
/// <see cref="JsonSerializer"/>.<c>Deserialize</c>.
///
/// <para>
/// When <c>JsonSerializer.Deserialize&lt;Dictionary&lt;string, object?&gt;&gt;</c> parses a JSON
/// document, each value is wrapped as <see cref="JsonElement"/> rather than the corresponding native
/// CLR type. The Json-mode form storage path returns those wrapped values; the Table-mode path
/// (<see cref="Interfaces.IFormDataStorage.ReadAsync"/>) returns native CLR via Dapper. Without
/// normalization, downstream consumers see different runtime types for the same logical field
/// depending on the source FormTemplate's StorageMode.
/// </para>
///
/// <para>
/// <b>P6-FU2 (2026-05-16):</b> introduced to close the JsonElement-vs-CLR asymmetry surfaced during BP
/// MVP P6. <b>Explicitly invalidates D9</b> ("no coercion, pass-through CLR") from the P6 plan — that
/// decision presumed both backends were uniform, which the JSON branch was not. The normalizer
/// converges both backends on native CLR shapes at the storage boundary, so the BP prefill resolver
/// (and any future consumer of <c>GetFullSubmissionAsync</c>) sees uniform types.
/// </para>
///
/// <para>Mapping (driven by <see cref="JsonElement.ValueKind"/>):</para>
/// <list type="bullet">
///   <item><description><c>String</c> → <see cref="string"/>.</description></item>
///   <item><description><c>Number</c> → <see cref="long"/> if it fits, then <see cref="decimal"/> if it fits, otherwise <see cref="double"/>.
///         <b>Decimal preferred over double</b> so read/write round-trip preserves precision for financial values.
///         This normalizer is now the single JsonElement→CLR implementation for the Forms module — the former
///         private copies in <c>FormDataPreprocessor</c> (write-path) and <c>FormulaBatchEvaluator</c> both delegate here.</description></item>
///   <item><description><c>True</c>/<c>False</c> → <see cref="bool"/>.</description></item>
///   <item><description><c>Null</c>/<c>Undefined</c> → <see langword="null"/>.</description></item>
///   <item><description><c>Array</c> → <see cref="List{T}"/> of recursively-normalized elements.</description></item>
///   <item><description><c>Object</c> → <see cref="Dictionary{TKey, TValue}"/> of recursively-normalized entries.</description></item>
/// </list>
///
/// <para>Non-<see cref="JsonElement"/> values pass through unchanged (Table-mode safety).</para>
///
/// <para>
/// <b>Performance:</b> O(total-node-count); single allocation per dict/list, no buffering.
/// Cost is microseconds vs the SQL round-trip that produced the input (milliseconds), so per-call
/// overhead is negligible even for wide forms with nested objects.
/// </para>
/// </summary>
public static class JsonValueNormalizer
{
    /// <summary>Normalize a single value. Non-<see cref="JsonElement"/> input passes through.</summary>
    public static object? Normalize(object? value)
    {
        if (value is not JsonElement element) return value;
        return NormalizeElement(element);
    }

    /// <summary>
    /// Normalize every value of a dictionary into a fresh <see cref="Dictionary{TKey, TValue}"/>.
    /// Keys are preserved verbatim; values are passed through <see cref="Normalize"/>.
    /// </summary>
    public static Dictionary<string, object?> NormalizeDict(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(source.Count);
        foreach (var kv in source)
            result[kv.Key] = Normalize(kv.Value);
        return result;
    }

    private static object? NormalizeElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String    => element.GetString(),
            JsonValueKind.Number    => NormalizeNumber(element),
            JsonValueKind.True      => true,
            JsonValueKind.False     => false,
            JsonValueKind.Null      => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Array     => element.EnumerateArray().Select(NormalizeElement).ToList(),
            JsonValueKind.Object    => NormalizeObject(element),
            _                       => element.ToString(),
        };

    private static Dictionary<string, object?> NormalizeObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
            dict[prop.Name] = NormalizeElement(prop.Value);
        return dict;
    }

    // Number coercion ladder: long → decimal → double. Matches FormDataPreprocessor write-path.
    // Double fallback covers exotic out-of-decimal-range values (|x| > ~7.9e28) without throwing.
    private static object NormalizeNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var i))    return i;
        if (element.TryGetDecimal(out var dec)) return dec;
        return element.GetDouble();
    }
}
