using System.Text.Json;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Parsed visible-stamp placement carried in <see cref="RequestSignatureInput.Placement"/> as JSON. The
/// coordinate contract is <b>bottom-left PDF points</b> (origin bottom-left, matching the writer's
/// <c>SignatureAppearance</c> rect) — any top-left flip is the caller/FE's responsibility.
///
/// <para>Parsing is <b>forward-compatible</b> (M6): unknown JSON fields are ignored so a newer FE can add keys
/// without breaking an older server. A null/blank/malformed payload — or a non-positive rectangle — yields
/// <c>false</c> from <see cref="TryParse"/>; the caller then signs invisibly (backward-compatible) rather than
/// failing the whole sign. Page/rect bounds against the real page MediaBox are enforced downstream by the
/// writer, fail-closed.</para>
/// </summary>
public sealed record SignaturePlacement(
    int Page,
    float X,
    float Y,
    float W,
    float H,
    Guid? ProfilePublicId,
    string? VisualType)
{
    // Case-insensitive so a {x,y,w,h} or {X,Y,W,H} payload both bind; unknown members are ignored by default
    // (no JsonUnmappedMemberHandling.Disallow), which is exactly the forward-compat behaviour we want (M6).
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Attempts to parse a placement JSON string. Returns false (and <paramref name="placement"/> = null) for
    /// null/blank/malformed input or a non-positive rectangle — the caller treats that as "no visible stamp
    /// requested" and signs invisibly.
    /// </summary>
    public static bool TryParse(string? json, out SignaturePlacement? placement)
    {
        placement = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var dto = JsonSerializer.Deserialize<PlacementDto>(json, Options);
            if (dto is null || dto.W <= 0 || dto.H <= 0) return false;
            placement = new SignaturePlacement(
                dto.Page, dto.X, dto.Y, dto.W, dto.H, dto.ProfilePublicId, dto.VisualType);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Optional-with-defaults so a partial payload (e.g. only x/y/w/h) parses; System.Text.Json honours record
    // constructor default values for missing members (.NET 8). Unknown members are ignored.
    private sealed record PlacementDto(
        int Page = 1,
        float X = 0,
        float Y = 0,
        float W = 0,
        float H = 0,
        Guid? ProfilePublicId = null,
        string? VisualType = null);
}
