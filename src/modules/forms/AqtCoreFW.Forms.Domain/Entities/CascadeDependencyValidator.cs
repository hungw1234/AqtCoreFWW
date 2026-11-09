using System.Text.Json;
using AqtCoreFW.Forms.Domain.Exceptions;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// Validates the cross-field cascade dependency GRAPH of a form at publish time (spike contract §4 —
/// whole-graph chokepoint, never at runtime).
///
/// A field's <c>DataSourceJson</c> may carry a
/// <c>"dependencies":[{ "dependsOnFieldKey": "...", "filterParamName": "..." }]</c> list. The graph
/// child→parent (an edge from a field to each field it dependsOn) must be:
///   - dangling-free: every <c>dependsOnFieldKey</c> resolves to an existing ACTIVE field IN THE SAME sub-graph,
///   - self-loop-free: a field may not depend on itself,
///   - acyclic: no directed cycle (arbitrary depth; topo-sort via Kahn's algorithm).
///
/// Absent/empty <c>dependencies</c> ⇒ the field is on the OLD static-fetch path (backward-safe
/// discriminator) and contributes no edges. The graph is validated as one TOP-LEVEL sub-graph
/// (ParentFieldId == null) plus one INDEPENDENT sub-graph per TableField (children grouped by
/// ParentFieldId, M1 slice Phase-06). Cross-TableField or top-level→in-row edges are dangling — the
/// row-scoped resolver at submit sees only the same-row parent value.
///
/// Throws <see cref="InvalidFormSchemaException"/> (GOV_FRM_04x) naming the offending field(s) so the
/// message surfaces to the admin. Kept as a pure static helper — no aggregate state, easy to unit-test.
/// </summary>
internal static class CascadeDependencyValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Validate the cascade graph over the given active fields — both the TOP-LEVEL sub-graph
    /// and each TableField's IN-ROW sub-graph (M1 slice, Phase-06). Throws on any violation.
    ///
    /// Each TableField is its OWN independent cascade sub-graph: a child column (ParentFieldId set)
    /// may only depend on OTHER child columns in the same TableField (same ParentFieldId). Cross-TF
    /// or top-level→in-row dependencies are dangling — the row-scoped resolver at submit sees only
    /// the same-row parent value. Child fields with empty <c>dependencies</c> = backward path, no edges.
    /// </summary>
    public static void Validate(IReadOnlyCollection<FormField> activeFields)
    {
        // Top-level sub-graph (ParentFieldId == null): the outer form's cascade.
        var topLevel = activeFields.Where(f => f.ParentFieldId is null).ToList();
        ValidateSubGraph(topLevel, "biểu mẫu");

        // Per-TableField sub-graphs: group children by ParentFieldId, run one topo-sort per group.
        // In-row dependencies must stay INSIDE the same TableField — cross-TF or top-level refs are
        // dangling within a sub-graph and rejected via the standard byKey lookup below.
        foreach (var group in activeFields
            .Where(f => f.ParentFieldId is not null)
            .GroupBy(f => f.ParentFieldId!.Value))
        {
            ValidateSubGraph(group.ToList(), $"bảng (ParentFieldId={group.Key})");
        }
    }

    /// <summary>Validate one independent cascade sub-graph — the top-level fields, or one TableField's
    /// child columns. Isolated from sibling sub-graphs by construction (caller groups the fields).</summary>
    private static void ValidateSubGraph(IReadOnlyList<FormField> fields, string scopeLabel)
    {
        var byKey = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);

        // edges[child] = set of parent fieldKeys the child dependsOn (within this sub-graph).
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var deps = ParseDependsOnKeys(field.DataSourceJson);
            if (deps.Count == 0)
                continue; // static-fetch path — no edges.

            var childKey = field.FieldKey;
            foreach (var parentKey in deps)
            {
                if (string.Equals(parentKey, childKey, StringComparison.Ordinal))
                    throw new InvalidFormSchemaException(
                        $"GOV_FRM_040: Field '{childKey}' không thể phụ thuộc chính nó (self-dependency).");

                if (!byKey.ContainsKey(parentKey))
                    throw new InvalidFormSchemaException(
                        $"GOV_FRM_041: Field '{childKey}' phụ thuộc field '{parentKey}' nhưng field này không tồn tại trong cùng {scopeLabel} (dangling reference).");

                (edges.TryGetValue(childKey, out var list)
                    ? list
                    : edges[childKey] = new List<string>()).Add(parentKey);
            }
        }

        DetectCycle(fields.Select(f => f.FieldKey).ToList(), edges);
    }

    /// <summary>
    /// Reject the deletion of <paramref name="fieldKeyToDelete"/> when a SURVIVING field still names it
    /// as a cascade parent — spike §4 decision "reject delete, do not silent-strip". Called from the
    /// field-deactivation path. Covers BOTH top-level and in-row (TableField-column) cascade edges (M1).
    /// </summary>
    public static void EnsureNotDependedOn(
        IReadOnlyCollection<FormField> activeFields, string fieldKeyToDelete)
    {
        foreach (var field in activeFields)
        {
            if (string.Equals(field.FieldKey, fieldKeyToDelete, StringComparison.Ordinal))
                continue;

            // M1 (Phase-06): child fields inside a TableField ALSO carry cascade edges — scan both
            // top-level and in-row so deleting a same-row cascade parent is rejected symmetrically.

            if (ParseDependsOnKeys(field.DataSourceJson)
                    .Any(k => string.Equals(k, fieldKeyToDelete, StringComparison.Ordinal)))
                throw new InvalidFormSchemaException(
                    $"GOV_FRM_042: Không thể xóa field '{fieldKeyToDelete}' vì field '{field.FieldKey}' đang phụ thuộc nó (cascade dependency).");
        }
    }

    /// <summary>
    /// Reject a PUBLIC form (<paramref name="isPublic"/>) carrying any active field whose external
    /// data-source config is Vault-authed (H2 — <c>AuthMode</c> present in DataSourceJson). Called from
    /// the Publish/Republish chokepoint alongside <see cref="Validate"/>.
    ///
    /// Rationale (C1×H2 intersection): a public form is reachable through the anonymous options endpoint;
    /// an authed-external DS there would read a Vault API key and issue an authenticated outbound fetch on
    /// behalf of an unauthenticated caller (SSRF / key-leak surface). Private (authenticated-only) forms
    /// are unaffected. No-op when the form is private or no field carries an authed-external config.
    /// </summary>
    public static void EnsureNoAuthedExternalOnPublic(IReadOnlyCollection<FormField> activeFields, bool isPublic)
    {
        if (!isPublic)
            return; // authed-external DS is allowed on private forms — nothing to enforce.

        // NOTE (scope): iterate ALL active fields, not just top-level (unlike Validate). The anonymous
        // options endpoint resolves a field by FieldKey+IsActive with NO top-level filter, so an in-row
        // (child) field carrying an authed-external DS is ALSO anonymously reachable and must be guarded.
        foreach (var field in activeFields)
        {
            if (IsAuthedExternalOnPublic(field.DataSourceJson))
                throw new InvalidFormSchemaException(
                    $"GOV_FRM_044: Field '{field.FieldKey}' dùng nguồn dữ liệu ngoài có xác thực (authed-external) " +
                    "không được phép trên biểu mẫu công khai (public form).");
        }
    }

    /// <summary>Kahn topo-sort; if any node remains with unresolved in-edges → a cycle exists.</summary>
    private static void DetectCycle(IReadOnlyList<string> nodes, Dictionary<string, List<string>> edges)
    {
        // in-degree = number of parents a child still waits on.
        var inDegree = nodes.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        foreach (var (child, parents) in edges)
            inDegree[child] = parents.Count;

        // reverse adjacency: parent → children that depend on it (to decrement as parents "resolve").
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (child, parents) in edges)
            foreach (var parent in parents)
                (dependents.TryGetValue(parent, out var l) ? l : dependents[parent] = new List<string>()).Add(child);

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var resolved = 0;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            resolved++;
            if (!dependents.TryGetValue(node, out var children))
                continue;
            foreach (var child in children)
                if (--inDegree[child] == 0)
                    queue.Enqueue(child);
        }

        if (resolved != nodes.Count)
        {
            var inCycle = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal);
            throw new InvalidFormSchemaException(
                $"GOV_FRM_043: Phát hiện vòng lặp phụ thuộc cascade giữa các field: {string.Join(", ", inCycle)}.");
        }
    }

    /// <summary>Extract dependsOnFieldKey values from a field's DataSourceJson. Empty/absent/malformed ⇒ [].</summary>
    private static List<string> ParseDependsOnKeys(string? dataSourceJson)
    {
        if (string.IsNullOrWhiteSpace(dataSourceJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(dataSourceJson);
            // JsonDocument property lookup is case-SENSITIVE (unlike JsonSerializer). DataSourceJson may be
            // written camelCase (FE + snapshot factory) or PascalCase (some configs) → match tolerantly.
            if (!TryGetPropertyCaseInsensitive(doc.RootElement, "dependencies", out var depsEl)
                || depsEl.ValueKind != JsonValueKind.Array)
                return [];

            var keys = new List<string>();
            foreach (var dep in depsEl.EnumerateArray())
            {
                if (dep.ValueKind == JsonValueKind.Object
                    && TryGetPropertyCaseInsensitive(dep, "dependsOnFieldKey", out var k)
                    && k.ValueKind == JsonValueKind.String)
                {
                    var key = k.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                        keys.Add(key);
                }
            }
            return keys;
        }
        catch (JsonException)
        {
            // Malformed DataSourceJson is caught by the resolver's own graceful-degradation path;
            // here it simply contributes no cascade edges.
            return [];
        }
    }

    /// <summary>Minimal mirror of the Infrastructure external-DS config, holding ONLY the auth marker.
    /// Deserialized with the SAME System.Text.Json options the resolver uses (case-insensitive,
    /// last-property-wins) so the guard's parse tolerance is byte-identical to the resolver's — a
    /// hand-rolled JsonDocument walk diverges on duplicate keys (JsonDocument = first-wins, STJ =
    /// last-wins), which would let an authed config slip past the guard yet authenticate at runtime.</summary>
    private sealed record ExternalAuthProbe(string? AuthMode);

    /// <summary>
    /// True when the field's DataSourceJson is a PUBLIC-form breach: an external DS carrying a non-empty
    /// <c>AuthMode</c> (the H2 Vault-authed marker). Absent/empty/whitespace AuthMode ⇒ false (static
    /// backward path — mirrors the resolver's <c>!IsNullOrWhiteSpace(config.AuthMode)</c> marker exactly).
    ///
    /// FAIL-CLOSED on unparseable config (malformed JSON, or AuthMode typed as number/bool that STJ cannot
    /// bind to string): return true so publish is REJECTED. Such a config cannot be safely served on a
    /// public form — at runtime the resolver would either throw an unhandled JsonException out of the
    /// anonymous endpoint (500 DoS) or, on a duplicate-key craft, bind last-wins and issue a Vault-authed
    /// fetch. Rejecting at publish makes "authed-or-unparseable external DS on a public form" an
    /// unrepresentable state. Private forms never reach here (caller short-circuits on !isPublic).
    /// </summary>
    private static bool IsAuthedExternalOnPublic(string? dataSourceJson)
    {
        if (string.IsNullOrWhiteSpace(dataSourceJson))
            return false;

        try
        {
            var probe = JsonSerializer.Deserialize<ExternalAuthProbe>(dataSourceJson, JsonOpts);
            return !string.IsNullOrWhiteSpace(probe?.AuthMode);
        }
        catch (JsonException)
        {
            // Unparseable / non-string AuthMode on a public form → fail closed (reject publish).
            return true;
        }
    }

    /// <summary>Case-insensitive property lookup on a JsonElement object (JsonDocument's own lookup is case-sensitive).</summary>
    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
