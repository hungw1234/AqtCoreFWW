using System.Globalization;
using System.Text.Json;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// H7: the SINGLE canonical principal serializer. Used by the resolver (P2 — snapshot
/// write into <c>WorkflowTask.CandidateRefsJson</c>) and the inbox principal projection
/// (P4 — read). Canonical forms: Guid → <c>"D"</c> lowercase; long → invariant culture.
/// Serialization is deterministic (stable key/element order) so the snapshot string is
/// a stable equality/audit value. No ad-hoc <c>.ToString()</c> elsewhere.
/// </summary>
public static class CandidatePrincipalSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Canonical principal ref for a Guid id (lowercase "D").</summary>
    public static string Ref(Guid id) => id.ToString("D").ToLowerInvariant();

    /// <summary>Canonical principal ref for a long id (invariant culture).</summary>
    public static string Ref(long id) => id.ToString(CultureInfo.InvariantCulture);

    public static string Serialize(IReadOnlyList<CandidatePredicate> predicates)
    {
        // M1: canonicalize order here (the single chokepoint) — callers feed unordered
        // collections (e.g. resolver's Dictionary.Values, no contracted enumeration order),
        // but the snapshot string must be a stable equality/audit/hash value (P4).
        var wire = predicates
            .OrderBy(p => (int)p.Type)
            .ThenBy(p => p.Ref, StringComparer.Ordinal)
            .ThenBy(p => (int)p.Combine)
            .Select(p => new WirePredicate((int)p.Type, p.Ref, (int)p.Combine))
            .ToList();
        return JsonSerializer.Serialize(wire, Options);
    }

    public static IReadOnlyList<CandidatePredicate> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var wire = JsonSerializer.Deserialize<List<WirePredicate>>(json, Options);
        if (wire is null || wire.Count == 0)
            return [];

        var result = new List<CandidatePredicate>(wire.Count);
        foreach (var w in wire)
            result.Add(new CandidatePredicate(
                (CandidatePrincipalType)w.t, w.r, (AssignmentCombineMode)w.c));
        return result;
    }

    // Compact, order-stable wire shape (t = principal type, r = canonical ref, c = combine).
    private sealed record WirePredicate(int t, string r, int c);
}
