using System.Reflection;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Singleton implementation of <see cref="IOutboxEventTypeResolver"/>. Builds a
/// FullName→Type map ONCE (in the constructor) from every concrete (non-abstract)
/// <see cref="IExternalDomainEvent"/> implementer found across the UNION of:
///   1. every already-loaded "AqtCoreFW.*" assembly, and
///   2. every "AqtCoreFW.*.Domain.dll" found on disk next to the running host.
///
/// The map is complete-by-construction, independent of module registration order/timing.
/// (1) alone is NOT sufficient: a module's ModuleRegistration.cs may reference only its
/// Application/Infrastructure assembly, never touching a type in its Domain assembly
/// directly — the CLR then has no reason to have loaded that Domain assembly yet, so an
/// external event declared there would be silently absent from the map. Since new rows
/// write a BARE FullName (no assembly component), the AssemblyQualifiedName fallback in
/// <see cref="Resolve"/> CANNOT recover a miss here either — <c>Type.GetType(bareName)</c>
/// only searches the calling assembly + corelib, never an arbitrary module's Domain
/// assembly. A map gap for a real external event is therefore a genuine silent-event-loss
/// bug, which is exactly what this resolver exists to eliminate — hence the unconditional
/// disk union (mirrors the pattern in Audit's DataClassificationService, but always runs,
/// not just when the loaded-assembly scan comes back empty).
///
/// BACKWARD-COMPAT: rows persisted before this stable-token change carry an
/// AssemblyQualifiedName. <see cref="Resolve"/> falls back to
/// <see cref="Type.GetType(string, bool)"/> for those legacy rows — transition safety, no
/// data migration required (greenfield deployment, no production data yet). This fallback
/// only ever applies to pre-cutover rows: a bare FullName written by the CURRENT
/// interceptor is resolved exclusively through the map above.
/// </summary>
public sealed class OutboxEventTypeResolver : IOutboxEventTypeResolver
{
    private const string AssemblyPrefix = "AqtCoreFW.";
    private const string DomainAssemblyDllPattern = AssemblyPrefix + "*.Domain.dll";

    private readonly IReadOnlyDictionary<string, Type> _byFullName;

    public OutboxEventTypeResolver()
    {
        var candidateTypes = ResolveCandidateAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, FullName: not null }
                && typeof(IExternalDomainEvent).IsAssignableFrom(t));

        _byFullName = BuildMap(candidateTypes);
    }

    /// <inheritdoc />
    public Type? Resolve(string token)
    {
        if (_byFullName.TryGetValue(token, out var type))
            return type;

        // Legacy fallback: rows persisted before the stable-token cutover carry an AQN.
        return Type.GetType(token, throwOnError: false);
    }

    /// <summary>
    /// Assembly set the resolver scans — every already-loaded "AqtCoreFW.*" assembly
    /// UNIONED (unconditionally, not just as an empty-result fallback) with every
    /// "AqtCoreFW.*.Domain.dll" on disk next to the host. Deduped by assembly SIMPLE NAME
    /// (not reference identity): if a Domain assembly is already loaded, it is never
    /// loaded a second time via <see cref="Assembly.LoadFrom(string)"/> — double-loading
    /// the same assembly under two different <see cref="Assembly"/> instances would make
    /// its own types look like a genuine duplicate-FullName collision to
    /// <see cref="BuildMap"/> below.
    /// </summary>
    private static IEnumerable<Assembly> ResolveCandidateAssemblies()
    {
        var byName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name;
            if (name is not null && name.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
                byName[name] = asm;
        }

        string[] domainDllPaths;
        try
        {
            domainDllPaths = Directory.GetFiles(AppContext.BaseDirectory, DomainAssemblyDllPattern);
        }
        catch (IOException)
        {
            domainDllPaths = [];
        }

        foreach (var path in domainDllPaths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (byName.ContainsKey(name)) continue; // already loaded — don't double-load

            try
            {
                byName[name] = Assembly.LoadFrom(path);
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException)
            {
                // Unloadable DLL (wrong arch, corrupt, missing dependency, etc.) — skip it.
                // A genuinely-needed event type in that assembly then surfaces as a
                // startup map gap (loud, visible in the dead-letter reason), not a
                // dispatcher crash.
            }
        }

        return byName.Values;
    }

    /// <summary>
    /// Builds the FullName→Type map, throwing if two different types report the same
    /// FullName — that would make the stable dispatch token ambiguous, which is a real
    /// authoring bug (rename one of the colliding types) rather than something the
    /// dispatcher can safely paper over.
    /// </summary>
    private static IReadOnlyDictionary<string, Type> BuildMap(IEnumerable<Type> candidateTypes)
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in candidateTypes)
        {
            var key = type.FullName!;
            if (map.TryGetValue(key, out var existing))
                throw new InvalidOperationException(
                    $"Outbox event-type token collision: '{key}' is declared by both " +
                    $"'{existing.AssemblyQualifiedName}' and '{type.AssemblyQualifiedName}'. " +
                    "Type.FullName is the stable Outbox dispatch token and must be unique " +
                    "across every IExternalDomainEvent implementer — rename one of the two types.");

            map[key] = type;
        }

        return map;
    }

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> throws <see cref="ReflectionTypeLoadException"/> when
    /// some types in the assembly fail to load (e.g. a missing optional dependency).
    /// Recover the types that DID load instead of aborting the whole scan.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
