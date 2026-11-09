using System.Text.Json;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Resolves data source options from a .NET enum type.
/// SECURITY: Only AqtCoreFW.* namespaces are allowed to prevent arbitrary type loading.
/// Config JSON: { "EnumTypeName": "AqtCoreFW.SomeModule.Domain.Enums.SomeEnum" }
///
/// Cascade v1 (keyed-enum filter): the config may also carry
///   "parentFilterMap": { "&lt;parentValue&gt;": ["&lt;allowed enum value&gt;", ...] }
/// When the caller supplies parentValues, the option-set is narrowed to the allowed values for those
/// parents. Enum values have no natural parent key, so the mapping is admin-configured here — no
/// cross-module enum change (spike §3 user decision 2026-08-05). Semantics:
///   - no parentValues (or no dependencies) ⇒ full enum set (backward guard),
///   - parentValues present ⇒ keep only values in the INTERSECTION of every supplied parent's allowed set,
///   - a parentValue absent from the map ⇒ fail-safe EMPTY (never leak the full set when a filter was intended).
/// </summary>
public sealed class EnumDataSourceResolver : IDataSourceResolver
{
    private const string AllowedNamespacePrefix = "AqtCoreFW.";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
        string dataSourceJson, Guid tenantId,
        IReadOnlyDictionary<string, string>? parentValues = null,
        CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<EnumDataSourceConfig>(dataSourceJson, JsonOpts);

        if (config is null || string.IsNullOrWhiteSpace(config.EnumTypeName))
            return Task.FromResult<IReadOnlyList<DataSourceOptionDto>>([]);

        // Security whitelist — only AqtCoreFW.* namespaces permitted
        if (!config.EnumTypeName.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Enum type '{config.EnumTypeName}' is not in an allowed namespace.");

        var enumType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t => t.FullName == config.EnumTypeName && t.IsEnum)
            ?? throw new InvalidOperationException(
                $"Enum type '{config.EnumTypeName}' not found in loaded assemblies.");

        var options = Enum.GetValues(enumType)
            .Cast<object>()
            .Select(v => new DataSourceOptionDto(
                Value: Convert.ToInt32(v).ToString(),
                Label: v.ToString()!))
            .ToList();

        var filtered = ApplyParentFilter(options, config.ParentFilterMap, parentValues);

        return Task.FromResult<IReadOnlyList<DataSourceOptionDto>>(filtered);
    }

    /// <summary>
    /// Cascade v1 keyed-enum filter. Pure, side-effect-free — extracted so the filter semantics are
    /// unit-testable without reflection/enum loading.
    /// </summary>
    internal static List<DataSourceOptionDto> ApplyParentFilter(
        List<DataSourceOptionDto> options,
        IReadOnlyDictionary<string, List<string>>? parentFilterMap,
        IReadOnlyDictionary<string, string>? parentValues)
    {
        // No parents supplied ⇒ full set (backward guard). Covers both "no dependencies" and
        // "parents not yet selected" — the endpoint gates the latter, but resolver stays safe alone.
        if (parentValues is null || parentValues.Count == 0)
            return options;

        // Parents supplied but the config carries no map ⇒ nothing to filter against ⇒ fail-safe empty.
        if (parentFilterMap is null || parentFilterMap.Count == 0)
            return [];

        // INTERSECTION of allowed value-sets across every supplied parent value.
        HashSet<string>? allowed = null;
        foreach (var parentValue in parentValues.Values)
        {
            if (!parentFilterMap.TryGetValue(parentValue, out var allowedForParent))
                return []; // parentValue not mapped ⇒ fail-safe empty.

            var set = new HashSet<string>(allowedForParent, StringComparer.Ordinal);
            if (allowed is null) allowed = set;
            else allowed.IntersectWith(set);
        }

        if (allowed is null || allowed.Count == 0)
            return [];

        return options.Where(o => allowed.Contains(o.Value)).ToList();
    }

    private sealed record EnumDataSourceConfig(
        string EnumTypeName,
        Dictionary<string, List<string>>? ParentFilterMap = null);
}
