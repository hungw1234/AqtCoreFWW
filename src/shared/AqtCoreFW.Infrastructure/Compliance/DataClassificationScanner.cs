using System.Reflection;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Infrastructure.Compliance;

/// <summary>
/// Scans assemblies for properties annotated with [DataClassification] and
/// produces a structured inventory report.
///
/// Usage (e.g. in a startup health check or a CI compliance script):
///   var report = DataClassificationScanner.ScanAssemblies(
///       typeof(CaseEntity).Assembly,
///       typeof(FileRecord).Assembly);
///   // report.Entries lists every classified property with type + level
/// </summary>
public static class DataClassificationScanner
{
    /// <summary>
    /// Scans the given assemblies and returns all properties tagged with
    /// [DataClassification], sorted by level (Restricted first) then by type name.
    /// </summary>
    public static DataClassificationReport ScanAssemblies(params Assembly[] assemblies)
    {
        var entries = new List<DataClassificationEntry>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attr = prop.GetCustomAttribute<DataClassificationAttribute>();
                    if (attr is null) continue;

                    entries.Add(new DataClassificationEntry(
                        AssemblyName: assembly.GetName().Name ?? assembly.FullName ?? "unknown",
                        TypeFullName: type.FullName ?? type.Name,
                        PropertyName: prop.Name,
                        PropertyType: prop.PropertyType.Name,
                        Level: attr.Level));
                }
            }
        }

        // Sort: most sensitive first, then alphabetically for stable output
        entries.Sort((a, b) =>
        {
            var levelCmp = b.Level.CompareTo(a.Level);
            if (levelCmp != 0) return levelCmp;
            var typeCmp = string.Compare(a.TypeFullName, b.TypeFullName, StringComparison.Ordinal);
            return typeCmp != 0 ? typeCmp : string.Compare(a.PropertyName, b.PropertyName, StringComparison.Ordinal);
        });

        return new DataClassificationReport(
            ScannedAt: DateTimeOffset.UtcNow,
            Entries: entries,
            TotalCount: entries.Count,
            RestrictedCount: entries.Count(e => e.Level == DataClassificationLevel.Restricted),
            ConfidentialCount: entries.Count(e => e.Level == DataClassificationLevel.Confidential));
    }

    /// <summary>
    /// Returns only entries at or above the given minimum level.
    /// Useful to find all Restricted+Confidential properties in one call.
    /// </summary>
    public static DataClassificationReport ScanAssemblies(
        DataClassificationLevel minimumLevel,
        params Assembly[] assemblies)
    {
        var full = ScanAssemblies(assemblies);
        var filtered = full.Entries.Where(e => e.Level >= minimumLevel).ToList();
        return full with
        {
            Entries = filtered,
            TotalCount = filtered.Count,
            RestrictedCount = filtered.Count(e => e.Level == DataClassificationLevel.Restricted),
            ConfidentialCount = filtered.Count(e => e.Level == DataClassificationLevel.Confidential)
        };
    }
}

/// <summary>Full scan result.</summary>
public sealed record DataClassificationReport(
    DateTimeOffset ScannedAt,
    List<DataClassificationEntry> Entries,
    int TotalCount,
    int RestrictedCount,
    int ConfidentialCount);

/// <summary>Single classified property found during scan.</summary>
public sealed record DataClassificationEntry(
    string AssemblyName,
    string TypeFullName,
    string PropertyName,
    string PropertyType,
    DataClassificationLevel Level);
