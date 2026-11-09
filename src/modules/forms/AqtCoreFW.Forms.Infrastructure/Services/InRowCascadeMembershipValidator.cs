using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Errors;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// M1 Phase-06 slice 6b: server-side membership guard for in-row TableField cascade child columns.
/// For each TableField with cascade-enabled child columns, iterate every submitted row, re-resolve
/// the child's option-set from the row's parent value via <see cref="IDataSourceResolverFactory"/>,
/// and reject Enum/Internal child values that fall outside the resolved set (422).
///
/// SEMANTIC NOTE (H-1, tracked v1.1): InternalDataSourceResolver currently IGNORES parentValues (its
/// cascade-filter path is DEFERRED, see InternalDataSourceResolver.cs). Until that ships, an Internal
/// child value only fails the guard when it falls outside the ENTIRE Internal domain - narrower
/// cross-parent tampering (value in domain but not in parent-narrowed subset) passes silently. Enum
/// path has the full parent-filter semantics. External path is soft-log regardless.
///
/// External resolver failures (upstream 5xx, timeout, SSRF-block) SOFT-LOG and do NOT block the submit:
/// N-rows × outbound HTTP is a DoS surface, so external-cascade trust stays at publish-time (Phase-06 user decision).
///
/// Fail-open guards:
///  - null tenantId (legacy caller / test without tenant context) → skip guard;
///  - null resolver factory (test DI-container without cascade wiring) → skip guard;
///  - child column with no <c>dependencies:[]</c> in DataSourceJson → skip (backward flat option-set).
/// </summary>
public sealed class InRowCascadeMembershipValidator
{
    private readonly IDataSourceResolverFactory _resolverFactory;
    private readonly ILogger<InRowCascadeMembershipValidator> _logger;

    public InRowCascadeMembershipValidator(
        IDataSourceResolverFactory resolverFactory,
        ILogger<InRowCascadeMembershipValidator> logger)
    {
        _resolverFactory = resolverFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs the guard against every TableField in <paramref name="fields"/> and appends
    /// membership-violation errors to <paramref name="errors"/>. Enum/Internal → hard-reject 422;
    /// External → soft-log warning, no error added.
    ///
    /// PERF (v1.1 FORMS-CASCADE-INROW-GUARD-N-PLUS-1-DEDUP): resolver is invoked once per distinct
    /// (child-field, parent-tuple) combination — NOT once per row. Complexity is O(distinct combos),
    /// not O(rows × cascade-children). Verdict (pass/fail) is preserved for every row: if any row's
    /// child value falls outside its parent-tuple's option-set, that violation surfaces as an error.
    /// One collapsing behavior worth naming: duplicate identical violations across rows (same child
    /// field, same parent-tuple, same bad child value) collapse to a single error entry rather than
    /// one per row — downstream today reads pass/fail, not error counts.
    /// </summary>
    public async Task ValidateAsync(
        IEnumerable<FormField> fields,
        IDictionary<string, object?> data,
        Guid tenantId,
        List<ValidationError> errors,
        CancellationToken cancellationToken = default)
    {
        var allFields = fields as IReadOnlyList<FormField> ?? fields.ToList();

        // Group child columns by their TableField parent — cascade in v1 is row-scoped and can only
        // reference sibling columns inside the same row.
        var tableFields = allFields.Where(f => f.Type == FormFieldType.TableField).ToList();
        if (tableFields.Count == 0) return;

        foreach (var table in tableFields)
        {
            var childColumns = allFields
                .Where(f => f.ParentFieldId == table.Id)
                .ToList();

            // Children with a non-empty `dependencies:[]` in DataSourceJson need the guard.
            var cascadeChildren = childColumns
                .Select(c => (Field: c, Dependencies: TryParseDependencies(c.DataSourceJson)))
                .Where(p => p.Dependencies.Count > 0)
                .ToList();

            if (cascadeChildren.Count == 0) continue;

            if (!data.TryGetValue(table.FieldKey, out var rawRows) || rawRows is null) continue;

            // Reuse the canonical row parser — handles JsonElement arrays, List<Dict>, and string JSON.
            var rows = FormDataPreprocessor.ParseTableFieldRows(rawRows);

            // ---- PASS 1: build dedup map keyed by (childFieldId, canonical-parent-tuple).
            // Each entry accumulates the distinct child values that need membership-checking against
            // the same resolved option-set. The captured parentValues dict is shared (all entries
            // under one key by definition carry identical parent values).
            var buckets = new Dictionary<DedupKey, DedupBucket>();

            foreach (var row in rows)
            {
                foreach (var (child, dependencies) in cascadeChildren)
                {
                    if (!row.TryGetValue(child.FieldKey, out var childRaw)) continue;
                    var childValue = childRaw?.ToString();
                    if (string.IsNullOrEmpty(childValue)) continue;

                    // Build parentValues dict — each dependency maps a parent column's row-value to a filterParamName.
                    var parentValues = BuildParentValues(dependencies, row, childColumns);
                    if (parentValues is null) continue; // parent value missing in this row → skip (no way to re-resolve)

                    // Dedup key uses child.FieldKey (domain-unique per form) rather than child.Id -
                    // aligns with the form invariant and stays stable across ephemeral test instances
                    // where the DB-assigned Id may not be set.
                    var key = new DedupKey(child.FieldKey, CanonicalizeParentTuple(parentValues));
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new DedupBucket(child, parentValues);
                        buckets[key] = bucket;
                    }
                    bucket.ChildValues.Add(childValue);
                }
            }

            if (buckets.Count == 0) continue;

            // ---- PASS 2: one resolver call per bucket; membership-check every distinct child value.
            foreach (var bucket in buckets.Values)
            {
                await ResolveAndCheckMembershipAsync(bucket, tenantId, errors, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Resolves the option-set once for a dedup bucket then applies the Enum/Internal (hard-reject)
    /// or External (soft-log) membership decision to every distinct child value in the bucket.
    /// </summary>
    private async Task ResolveAndCheckMembershipAsync(
        DedupBucket bucket,
        Guid tenantId,
        List<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        var child = bucket.Child;

        // Defense-in-depth: DataSourceResolverFactory throws ArgumentException for non-Ref field types.
        // Publish-time CascadeDependencyValidator does NOT gate on FormFieldType (accepts any field with
        // valid graph shape), so a hand-crafted DataSourceJson with `dependencies:[]` on a Text/Date child
        // would reach this method and crash the entire submit. Skip guard for unsupported types instead.
        if (child.Type is not (FormFieldType.EnumRef or FormFieldType.InternalRef or FormFieldType.ExternalRef))
        {
            _logger.LogWarning(
                "In-row cascade dependencies present on unsupported field type '{Type}' for field '{FieldKey}' - skipping guard (should be blocked at publish time).",
                child.Type, child.FieldKey);
            return;
        }

        var isExternal = child.Type == FormFieldType.ExternalRef;

        try
        {
            var resolver = _resolverFactory.Create(child.Type);
            var options = await resolver.ResolveAsync(
                child.DataSourceJson ?? string.Empty, tenantId, bucket.ParentValues, cancellationToken);

            // Materialize allowed-values into a HashSet (Ordinal) so each child-value lookup is O(1)
            // — the bucket may hold hundreds of distinct child values under adversarial N-row input.
            var allowed = new HashSet<string>(options.Select(o => o.Value), StringComparer.Ordinal);

            foreach (var childValue in bucket.ChildValues)
            {
                if (allowed.Contains(childValue)) continue;

                if (isExternal)
                {
                    // External soft-log — do not block submit.
                    _logger.LogWarning(
                        "In-row cascade membership violation on external field '{FieldKey}' (value='{Value}'); soft-log per Phase-06 policy.",
                        child.FieldKey, childValue);
                }
                else
                {
                    // Enum / Internal hard-reject.
                    errors.Add(new ValidationError(
                        $"Field '{child.FieldKey}': giá trị cascade không thuộc tập cha đã chọn.",
                        child.FieldKey));
                }
            }
        }
        catch (Exception ex) when (isExternal)
        {
            // External resolver crashed (upstream 5xx, SSRF-block, timeout) → soft-log, don't block.
            // Dedup collapses N-row bursts to one soft-log per (child, parent-tuple).
            _logger.LogWarning(ex,
                "External cascade resolver failed for field '{FieldKey}'; membership check skipped (soft-log).",
                child.FieldKey);
        }
    }

    /// <summary>
    /// Canonicalizes the parentValues dict into a collision-safe string suitable as a dedup key.
    /// Sorted by key ASC (Ordinal); each key/value is <see cref="Uri.EscapeDataString(string)"/>-encoded before
    /// joining with the reserved delimiters <c>=</c> (pair) and <c>|</c> (pair-separator). Percent-encoding
    /// guarantees literal <c>|</c>/<c>=</c> inside a user-supplied parent value cannot forge a delimiter
    /// and collide two semantically-different tuples onto the same bucket — a bypass class the guard
    /// itself was written to prevent (multi-parent path: two rows whose raw text canonicalizes identically
    /// would silently share the FIRST row's resolved option-set, defeating server-side membership).
    /// </summary>
    private static string CanonicalizeParentTuple(IReadOnlyDictionary<string, string> parentValues)
    {
        // Small dicts (v1 cascade rarely > 3 parents) — sort in-place, no LINQ allocation pressure.
        if (parentValues.Count == 0) return string.Empty;
        var pairs = parentValues.ToArray();
        Array.Sort(pairs, (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return string.Join('|', pairs.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    /// <summary>Dedup key: same child field + same canonicalized parent tuple ⇒ resolve once.</summary>
    private readonly record struct DedupKey(string ChildFieldKey, string CanonicalParentTuple);

    /// <summary>Bucket accumulating the distinct child values that share one resolve call.</summary>
    private sealed class DedupBucket
    {
        public FormField Child { get; }
        public IReadOnlyDictionary<string, string> ParentValues { get; }
        public HashSet<string> ChildValues { get; }

        public DedupBucket(FormField child, IReadOnlyDictionary<string, string> parentValues)
        {
            Child = child;
            ParentValues = parentValues;
            ChildValues = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Builds the parentValues dict passed to <see cref="IDataSourceResolver.ResolveAsync"/>.
    /// Returns null when any required parent value is missing from the row — the caller then skips
    /// re-resolve (we cannot know what set the child should belong to without the parent).
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildParentValues(
        IReadOnlyList<Dependency> dependencies,
        IDictionary<string, object?> row,
        IReadOnlyList<FormField> siblingColumns)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dep in dependencies)
        {
            // Parent must be a sibling column in the same TableField — cascade is row-scoped in v1.
            var parentCol = siblingColumns.FirstOrDefault(f => f.FieldKey == dep.DependsOnFieldKey);
            if (parentCol is null) return null;

            if (!row.TryGetValue(dep.DependsOnFieldKey, out var parentRaw)) return null;
            var parentValue = parentRaw?.ToString();
            if (string.IsNullOrEmpty(parentValue)) return null;

            result[dep.FilterParamName] = parentValue;
        }
        return result;
    }

    /// <summary>
    /// Parses the <c>dependencies:[]</c> array out of the child's DataSourceJson. Returns an empty
    /// list when the JSON is null, malformed, or the discriminator is absent (backward flat path).
    /// </summary>
    private static IReadOnlyList<Dependency> TryParseDependencies(string? dataSourceJson)
    {
        if (string.IsNullOrWhiteSpace(dataSourceJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(dataSourceJson);
            if (!doc.RootElement.TryGetProperty("dependencies", out var depsEl)
                || depsEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<Dependency>();
            foreach (var el in depsEl.EnumerateArray())
            {
                if (!el.TryGetProperty("dependsOnFieldKey", out var keyEl)
                    || !el.TryGetProperty("filterParamName", out var paramEl))
                {
                    continue;
                }
                var key = keyEl.GetString();
                var param = paramEl.GetString();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(param)) continue;
                list.Add(new Dependency(key, param));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    private readonly record struct Dependency(string DependsOnFieldKey, string FilterParamName);
}
