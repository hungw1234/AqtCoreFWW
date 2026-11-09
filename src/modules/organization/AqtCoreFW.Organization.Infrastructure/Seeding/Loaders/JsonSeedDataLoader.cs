using System.Reflection;
using System.Text.Json;
using AqtCoreFW.Organization.Infrastructure.Seeding.Models;

namespace AqtCoreFW.Organization.Infrastructure.Seeding.Loaders;

/// <summary>Loads embedded JSON seed resources.</summary>
public static class JsonSeedDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Load OrgUnitSeedDto array from embedded resource.
    /// Resource name format: "AqtCoreFW.Organization.Infrastructure.Seeding.Resources.{fileName}"
    /// </summary>
    public static OrgUnitSeedDto[] LoadFromResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"AqtCoreFW.Organization.Infrastructure.Seeding.Resources.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded resource not found: {resourceName}");

        return JsonSerializer.Deserialize<OrgUnitSeedDto[]>(stream, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize: {resourceName}");
    }

    /// <summary>Get all available seed resource names.</summary>
    public static IEnumerable<string> GetAvailableResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "AqtCoreFW.Organization.Infrastructure.Seeding.Resources.";
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..]);
    }
}
