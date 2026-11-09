using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Architecture.Tests.Helpers;

/// <summary>
/// Shared reflection used by the Presentation-layer controller-placement guards (ADR-011: G1/G3).
///
/// Extracts the types a controller is DI-COUPLED to — its public-constructor parameter types
/// (C# primary-constructor params surface as constructor params too) plus the generic arguments
/// of those parameters — and classifies assemblies as a module Infrastructure layer or a module
/// owner. Constructor injection is the deliberate signal: it is the structural coupling an arch
/// guard can enforce. Method-body and service-locator usages
/// (<c>HttpContext.RequestServices.GetRequiredService&lt;T&gt;()</c>) are intentionally NOT seen
/// here — that blind spot is documented in ADR-011 and closed for the one offender
/// (<c>AccountMfaController</c>) by a code refactor, not by widening this reflection.
/// </summary>
internal static class ControllerDependencyReflection
{
    // A *module* infrastructure assembly: AqtCoreFW.<Module>.Infrastructure (exactly 3 dotted
    // segments after the root). The shared composition-root infra assembly AqtCoreFW.Infrastructure
    // (2 segments) is NOT a module layer — host admin controllers may legitimately depend on it
    // (ADR-011 rule 3: shared infra has no owning module / Presentation layer), so it is excluded.
    private static readonly Regex ModuleInfrastructure =
        new(@"^AqtCoreFW\.[A-Za-z]+\.Infrastructure$", RegexOptions.Compiled);

    // A module-owned assembly of any layer: AqtCoreFW.<Module>.<Layer>. Captures <Module> so a
    // controller's "module footprint" can be computed (G3). SharedKernel + the shared infra
    // assembly + the host assemblies do not match → they never count toward a footprint.
    private static readonly Regex ModuleLayer =
        new(@"^AqtCoreFW\.(?<module>[A-Za-z]+)\.(Domain|Application|Infrastructure|Presentation|Contracts)$",
            RegexOptions.Compiled);

    /// <summary>True iff the assembly is a module Infrastructure layer (NOT the shared infra).</summary>
    public static bool IsModuleInfrastructureAssembly(Assembly assembly) =>
        ModuleInfrastructure.IsMatch(assembly.GetName().Name ?? string.Empty);

    /// <summary>The module token (e.g. "Identity") if the assembly is a module layer, else null.</summary>
    public static string? ModuleToken(Assembly assembly)
    {
        var match = ModuleLayer.Match(assembly.GetName().Name ?? string.Empty);
        return match.Success ? match.Groups["module"].Value : null;
    }

    /// <summary>
    /// Distinct types the controller is DI-coupled to: every public-constructor parameter type,
    /// plus the generic type arguments of those parameters (recursively) so that
    /// <c>IOptionsMonitor&lt;ViettelMysignOptions&gt;</c> surfaces <c>ViettelMysignOptions</c> and
    /// a hypothetical <c>Repository&lt;Entity&gt;</c> surfaces <c>Entity</c>.
    /// </summary>
    public static IEnumerable<Type> GetInjectedDependencyTypes(Type controller)
    {
        var seen = new HashSet<Type>();
        foreach (var ctor in controller.GetConstructors())
        {
            foreach (var parameter in ctor.GetParameters())
            {
                foreach (var type in ExpandType(parameter.ParameterType))
                {
                    if (seen.Add(type))
                        yield return type;
                }
            }
        }
    }

    /// <summary>The set of module tokens a host controller is constructor-coupled to (its footprint).</summary>
    public static IReadOnlySet<string> ModuleFootprint(Type controller) =>
        GetInjectedDependencyTypes(controller)
            .Select(t => ModuleToken(t.Assembly))
            .Where(token => token is not null)
            .Select(token => token!)
            .ToHashSet();

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                foreach (var inner in ExpandType(arg))
                    yield return inner;
            }
        }
    }

    /// <summary>Every concrete, non-abstract <see cref="ControllerBase"/> subtype in the assembly.</summary>
    public static IEnumerable<Type> DiscoverControllers(Assembly assembly) =>
        SafeGetTypes(assembly).Where(t =>
            t is { IsAbstract: false, IsClass: true, IsGenericTypeDefinition: false } &&
            typeof(ControllerBase).IsAssignableFrom(t));

    /// <summary>
    /// Tolerant <see cref="Assembly.GetTypes"/> — a partial type-load (missing optional dependency)
    /// yields a <see cref="ReflectionTypeLoadException"/> whose <c>Types</c> array still holds the
    /// resolvable types. Mirrors the hardening in ModuleStartupWiringTests / the shared-infra guard.
    /// </summary>
    public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
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
