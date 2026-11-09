using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Reads a JSON string-typed field that Viettel may serialize as a bare NUMBER.
///
/// The vendor ignores its own spec's "all fields are strings" claim — login's
/// <c>expires_in</c> arrived as the number 3393, not "3393". The string-typed fields whose
/// values are pure integers — <c>status</c> ("1"/"4000"/"50000") and <c>error</c> ("58100") —
/// are the most likely to arrive numeric. Without this, System.Text.Json throws
/// "could not be converted to System.String" and breaks the status-poll loop / error surfacing.
///
/// <c>JsonNumberHandling.AllowReadingFromString</c> does NOT cover this case — it is the reverse
/// direction (string token → number property), not number token → string property.
///
/// Applied at the property level so it covers every deserialization path (signHash response and
/// the /requests/status poll) regardless of the per-client <see cref="JsonSerializerOptions"/>.
/// </summary>
internal sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            // Return the raw numeric literal verbatim — never parse. GetInt64/GetDecimal would throw
            // FormatException/OverflowException (NOT JsonException) on an out-of-range token, which
            // escapes the clients' catch(JsonException) and breaks the poll loop — the exact failure
            // this converter exists to prevent. Raw text is also the faithful string ("4000" stays "4000").
            JsonTokenType.Number => ReadNumberLiteral(ref reader),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException(
                $"Unexpected token {reader.TokenType} when reading a string-typed Viettel Mysign field"),
        };

    private static string ReadNumberLiteral(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence)
            return Encoding.UTF8.GetString(reader.ValueSpan);

        var seq = reader.ValueSequence;
        return Encoding.UTF8.GetString(BuffersExtensions.ToArray(in seq));
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
