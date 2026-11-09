using System.Text.Json;

namespace AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;

/// <summary>
/// Allowlist of completed Phase-4 batches. Phase-3 ships allowlist EMPTY — rules apply only to V7GuidGenerator (foundation)
/// and are trivially clean. Each Phase-4 batch commit appends entity / module names; Phase-8 finalize asserts allowlist
/// covers every entity in entity-tier-mapping-data.csv (full enforcement gate).
///
/// JSON shape: { "modules": ["identity", "forms"], "entities": ["Tenant", "FormSubmission"] }
///
/// Phase-3 (2026-05-03): RED-TEAM-11 + H3 amendment — allowlist replaces [Skip] anti-pattern. Skipped tests led
/// to v2.133p9 incident (memory: feedback-architecture-tests-in-finalize-gate); allowlist enforces TODAY for completed batches.
/// </summary>
public sealed class AllowlistRegistry
{
    public IReadOnlySet<string> Modules { get; }
    public IReadOnlySet<string> Entities { get; }

    private AllowlistRegistry(IReadOnlySet<string> modules, IReadOnlySet<string> entities)
    {
        Modules = modules;
        Entities = entities;
    }

    public static AllowlistRegistry Load()
    {
        var root = SourceCodeScanner.LocateRepoRoot();
        var path = Path.Combine(root, "tests", "unit", "AqtCoreFW.SharedKernel.Tests", "Architecture", "architecture-test-allowlist.json");
        if (!File.Exists(path))
            return new AllowlistRegistry(new HashSet<string>(), new HashSet<string>());

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var modules = ReadStringArray(doc.RootElement, "modules");
        var entities = ReadStringArray(doc.RootElement, "entities");
        return new AllowlistRegistry(modules, entities);
    }

    public bool IsEntityAllowlisted(string entityName) => Entities.Contains(entityName);
    public bool IsModuleAllowlisted(string moduleName) => Modules.Contains(moduleName);

    public bool IsEmpty => Modules.Count == 0 && Entities.Count == 0;

    private static IReadOnlySet<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new HashSet<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        }
        return set;
    }
}
