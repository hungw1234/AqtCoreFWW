namespace AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;

/// <summary>
/// Loads Phase-1 entity tier classification from `plans/260503-0045-foundation-perf-surgery/research/entity-tier-mapping-data.csv`.
///
/// Schema: Module,Entity,Tier,Sensitivity,Rationale,Exposure
/// Tier values: T1 (internal/lookup) | T2 (API-exposed aggregate) | T3 (event/audit/append-log)
/// Sensitivity values: v4 | v7
///
/// Phase-3 (2026-05-03): rules consume this registry to scope assertions per tier — prevents junction T1
/// false positives on T2-only rules (M3+M4 amendment).
/// </summary>
public sealed record EntityTierEntry(string Module, string Entity, string Tier, string Sensitivity, string Rationale, string Exposure);

public sealed class T2EntityRegistry
{
    private readonly IReadOnlyList<EntityTierEntry> _entries;

    private T2EntityRegistry(IReadOnlyList<EntityTierEntry> entries) => _entries = entries;

    public static T2EntityRegistry Load()
    {
        var root = SourceCodeScanner.LocateRepoRoot();
        var csv = Path.Combine(root, "plans", "260503-0045-foundation-perf-surgery", "research", "entity-tier-mapping-data.csv");
        if (!File.Exists(csv))
            throw new FileNotFoundException($"Entity tier CSV missing: {csv}");

        var entries = new List<EntityTierEntry>();
        var lines = File.ReadAllLines(csv);
        for (var i = 1; i < lines.Length; i++) // skip header
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var cols = SplitCsvLine(line);
            if (cols.Length < 4) continue;

            entries.Add(new EntityTierEntry(
                Module:      cols[0],
                Entity:      cols[1],
                Tier:        cols[2],
                Sensitivity: cols[3],
                Rationale:   cols.Length > 4 ? cols[4] : string.Empty,
                Exposure:    cols.Length > 5 ? cols[5] : string.Empty));
        }
        return new T2EntityRegistry(entries);
    }

    public IReadOnlyList<EntityTierEntry> All => _entries;
    public IReadOnlyList<EntityTierEntry> T1() => _entries.Where(e => e.Tier == "T1").ToList();
    public IReadOnlyList<EntityTierEntry> T2() => _entries.Where(e => e.Tier == "T2").ToList();
    public IReadOnlyList<EntityTierEntry> T3() => _entries.Where(e => e.Tier == "T3").ToList();
    public IReadOnlyList<EntityTierEntry> SensitiveV4() => _entries.Where(e => e.Sensitivity == "v4").ToList();

    /// <summary>Naïve CSV line split honoring double-quoted commas. Adequate for our schema (no embedded newlines).</summary>
    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
