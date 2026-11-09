using System.Text.Json;

namespace AqtCoreFW.BusinessProcess.Application.ModuleClients;

/// <summary>
/// Envelope v1 of the per-field prefill pointer stored in <c>FormField.PrefillSourceJson</c>.
/// Resolver reads this from the published <c>FormTemplateVersion.SchemaJson</c> snapshot, not the
/// live entity — so live edits only take effect on the next Publish/Republish.
/// </summary>
public sealed record PrefillSourceMeta(Guid SourceFormTemplatePublicId, string SourceFieldKey)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parse a single PrefillSource JSON string into the envelope. Returns <c>null</c> on any of:
    ///   - null/whitespace input (no prefill configured)
    ///   - malformed JSON (logged + skipped by the resolver, partial-fail tolerance per D4)
    ///   - missing <c>sourceFormTemplateId</c> or <c>sourceFieldKey</c> keys
    ///   - <c>Guid.Empty</c> source template id (sentinel for unset)
    /// Caller decides how to log; this method never throws.
    /// </summary>
    public static PrefillSourceMeta? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            // Wire format keeps client-friendly camelCase; envelope ignores extra keys for forward-compat.
            var raw = JsonSerializer.Deserialize<RawPayload>(json, JsonOpts);
            if (raw is null) return null;
            if (raw.SourceFormTemplateId == Guid.Empty) return null;
            if (string.IsNullOrWhiteSpace(raw.SourceFieldKey)) return null;
            return new PrefillSourceMeta(raw.SourceFormTemplateId, raw.SourceFieldKey);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RawPayload(Guid SourceFormTemplateId, string SourceFieldKey);
}
