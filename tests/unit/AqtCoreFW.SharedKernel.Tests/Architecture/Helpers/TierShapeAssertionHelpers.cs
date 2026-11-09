using System.Reflection;

namespace AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;

/// <summary>
/// Assembly loading + entity type lookup helpers for IL-shape tier rules (R6/R7/R8).
///
/// Strategy: Module Domain DLLs are NOT project-referenced by this test project.
/// We resolve them at runtime by probing the repo tree (Debug or Release bin output).
/// Type lookup is case-insensitive to tolerate minor CSV casing drift.
/// </summary>
public static class TierShapeAssertionHelpers
{
    // Cache loaded assemblies to avoid repeated disk probes in tight loops.
    private static readonly Dictionary<string, Assembly?> _asmCache = new(StringComparer.OrdinalIgnoreCase);

    // Framework names to check for the output DLL (prefer Debug; fall back to Release).
    private static readonly string[] Tfms = ["net10.0", "net9.0", "net8.0"];
    private static readonly string[] Configs = ["Debug", "Release"];

    /// <summary>
    /// Locates the entity type by scanning the module's Domain assembly.
    /// Returns null if the assembly cannot be loaded (Phase-08 coverage fact will catch gaps).
    /// </summary>
    public static Type? FindEntityType(string moduleName, string entityName)
    {
        var asm = ResolveModuleAssembly(moduleName);
        if (asm is null) return null;

        return asm.GetTypes().FirstOrDefault(t =>
            string.Equals(t.Name, entityName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the Domain assembly for a given module name (e.g. "identity" → "AqtCoreFW.Identity.Domain").
    /// Probes:
    ///   1. Already-loaded assemblies in the current AppDomain.
    ///   2. Disk: {repoRoot}/src/modules/{module}/{AsmName}/bin/{Config}/{tfm}/{AsmName}.dll
    /// </summary>
    private static Assembly? ResolveModuleAssembly(string moduleName)
    {
        // Normalize: "identity" → "Identity"
        var moduleProper = Capitalize(moduleName);
        var asmName = $"AqtCoreFW.{moduleProper}.Domain";

        if (_asmCache.TryGetValue(asmName, out var cached)) return cached;

        // 1. Already loaded (e.g. if another test forced it in).
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, asmName, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null)
        {
            _asmCache[asmName] = loaded;
            return loaded;
        }

        // 2. Try Assembly.Load (works if DLL is in probing path or GAC-adjacent).
        try
        {
            var asm = Assembly.Load(asmName);
            _asmCache[asmName] = asm;
            return asm;
        }
        catch { /* fall through to disk probe */ }

        // 3. Disk probe via repo root.
        try
        {
            var root = SourceCodeScanner.LocateRepoRoot();
            // Canonical module path pattern: src/modules/{module}/{AsmName}/
            var moduleDir = Path.Combine(root, "src", "modules", moduleName.ToLowerInvariant(), asmName);
            if (!Directory.Exists(moduleDir))
            {
                // Fallback: try exact case from CSV (some modules may use mixed case dirs)
                moduleDir = Path.Combine(root, "src", "modules", moduleProper, asmName);
            }

            foreach (var cfg in Configs)
            foreach (var tfm in Tfms)
            {
                var dllPath = Path.Combine(moduleDir, "bin", cfg, tfm, $"{asmName}.dll");
                if (!File.Exists(dllPath)) continue;

                var asm = Assembly.LoadFrom(dllPath);
                _asmCache[asmName] = asm;
                return asm;
            }
        }
        catch { /* unloadable DLL — skip */ }

        // Assembly not found — rule will silently skip this entity.
        _asmCache[asmName] = null;
        return null;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
