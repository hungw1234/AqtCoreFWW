using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.Architecture.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guard G1 (ADR-011 rule 1 — "thin controllers, no Infrastructure").
///
/// A <see cref="Microsoft.AspNetCore.Mvc.ControllerBase"/> across ANY controller-hosting assembly
/// (host <c>AqtCoreFW.Api</c> + every module <c>*.Presentation</c>) MUST NOT be constructor-coupled
/// to a <see cref="DbContext"/> subtype or to any type in a module Infrastructure assembly
/// (<c>AqtCoreFW.&lt;Module&gt;.Infrastructure</c>). Writes belong behind <c>ISender.Send</c> so the
/// MediatR pipeline (AuditBehavior + ValidationBehavior + TenantAwareBehavior) applies — the exact
/// guarantees the DbContext-direct signing controllers bypass today.
///
/// Scope clarifications:
///   • Domain repositories are allowed. G1 checks DbContext + module Infrastructure ONLY; it does NOT
///     flag a <c>*.Domain.Repositories</c> interface (<c>ConsentController</c> injects
///     <c>IConsentRepository</c> — an accepted read pattern). The invariant text matches what the
///     guard enforces (no "no repositories" rule the guard cannot back).
///   • The SHARED composition-root infra assembly <c>AqtCoreFW.Infrastructure</c> is excluded — host
///     admin controllers over shared infra (no owning module) are legitimate residents (rule 3).
///   • Reflection caveat (documented, not a bug): only constructor injection is inspected. A
///     service-locator pull (<c>HttpContext.RequestServices.GetRequiredService&lt;T&gt;()</c>) is
///     invisible here — <c>AccountMfaController</c> uses one for <c>UserManager</c>; that gap is
///     closed by refactoring the controller, not by widening this guard.
///
/// Allowlist: seeded EMPIRICALLY from a current-tree run, not an assumed list. Each fix phase removes
/// its own entries in the SAME commit as the controller fix; the empty-allowlist end-state is asserted
/// once, in the Phase-6 closure test.
/// </summary>
public sealed class ControllersDoNotDependOnInfrastructureGuardTests
{
    /// <summary>
    /// Controllers known to depend on a DbContext / module Infrastructure type today. Key = FullName,
    /// Value = reason + the phase that removes it. Shrinks to EMPTY by the end of the remediation.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownInfraCoupledControllers =
        new Dictionary<string, string>(); // empty — no controller is coupled to a module Infrastructure type

    [Fact]
    public void NoController_DependsOnDbContextOrModuleInfrastructure()
    {
        var violations = DiscoverViolations();

        violations.Should().BeEmpty(
            "a ControllerBase must not be constructor-coupled to a DbContext or a module " +
            "*.Infrastructure type — route writes through ISender so AuditBehavior/ValidationBehavior " +
            "apply. New violations: {0}. " +
            "(If this is a known pre-existing violator being fixed across the remediation, it must be on " +
            "the KnownInfraCoupledControllers allowlist until its fix phase removes it.)",
            string.Join("; ", violations.Select(v => $"{v.Controller} → {v.OffendingType}")));
    }

    [Fact]
    public void Closure_AllowlistIsEmpty()
    {
        // The remediation drove every controller-to-module-Infrastructure coupling to zero. Pin the
        // empty end-state so re-introducing an allowlist entry is a conscious, reviewed decision rather
        // than a silent weakening of the guard.
        KnownInfraCoupledControllers.Should().BeEmpty(
            "no controller may be coupled to a module Infrastructure type; adding an allowlist entry " +
            "must be a deliberate, documented exception");
    }

    [Fact]
    public void Discovery_FindsAControllerPopulation()
    {
        // Anti-vacuous: if reflection returns nothing (assemblies not loaded, base-type check broken),
        // the violation check above passes for the WRONG reason. Pin a realistic controller floor.
        var controllers = AssemblyFixtures.PresentationAssemblies
            .SelectMany(ControllerDependencyReflection.DiscoverControllers)
            .ToList();

        controllers.Count.Should().BeGreaterThanOrEqualTo(30,
            "the host + 15 module Presentation layers expose well over 30 controllers; a smaller count " +
            "means discovery silently under-reflected and the guard is vacuous");
    }

    [Fact]
    public void Guard_FlagsASyntheticModuleInfrastructureCoupledController()
    {
        // Teeth proof: exercise the SAME classifier the main test uses against a synthetic controller
        // coupled to a REAL module-Infrastructure type (IdentityDbContext lives in
        // AqtCoreFW.Identity.Infrastructure), so a future refactor neutering the classifier fails HERE.
        var offending = ClassifyOffendingType(typeof(FakeModuleInfraCoupledController));

        offending.Should().NotBeNull(
            "a controller injecting a module Infrastructure type must be classified as a violation");
    }

    [Fact]
    public void Allowlist_EntriesAreStillRealViolations()
    {
        // Tripwire (M1c): a name-only existence check passes vacuously after a controller is fixed or
        // relocated (its FullName changes with the namespace). Require each allowlisted controller to
        // (a) still be discoverable across the controller-hosting assemblies AND (b) still be actually
        // Infrastructure-coupled. Once a fix phase removes the coupling (or relocates+cleans), the
        // lingering entry FAILS here, forcing allowlist cleanup in the same commit.
        var byName = AssemblyFixtures.PresentationAssemblies
            .SelectMany(ControllerDependencyReflection.DiscoverControllers)
            .GroupBy(t => t.FullName)
            .ToDictionary(g => g.Key!, g => g.First());

        var stale = KnownInfraCoupledControllers.Keys
            .Where(name => !byName.TryGetValue(name, out var controller) ||
                           ClassifyOffendingType(controller) is null)
            .ToList();

        stale.Should().BeEmpty(
            "an allowlist entry must name a controller that still exists AND is still Infrastructure-" +
            "coupled. Stale entries (fixed/relocated/removed): {0}",
            string.Join(", ", stale));
    }

    // ── Classifier (shared by main + teeth tests) ───────────────────────────────

    private static List<(string Controller, string OffendingType)> DiscoverViolations()
    {
        var violations = new List<(string, string)>();

        foreach (var assembly in AssemblyFixtures.PresentationAssemblies)
        {
            foreach (var controller in ControllerDependencyReflection.DiscoverControllers(assembly))
            {
                var fullName = controller.FullName ?? controller.Name;
                if (KnownInfraCoupledControllers.ContainsKey(fullName))
                    continue;

                var offending = ClassifyOffendingType(controller);
                if (offending is not null)
                    violations.Add((fullName, offending));
            }
        }

        return violations;
    }

    /// <summary>
    /// The first module-Infrastructure dependency of a controller, or null. A module DbContext
    /// (e.g. IdentityDbContext) is caught because it lives in a module <c>*.Infrastructure</c> assembly.
    /// A SHARED-infra DbContext (AlertingDbContext etc. in <c>AqtCoreFW.Infrastructure</c>) is
    /// deliberately NOT flagged — host admin controllers over shared infra are accepted residents
    /// (ADR-011 rule 3 / out-of-scope), so the discriminator is "module infrastructure", not "any
    /// DbContext".
    /// </summary>
    private static string? ClassifyOffendingType(Type controller)
    {
        foreach (var dependency in ControllerDependencyReflection.GetInjectedDependencyTypes(controller))
        {
            if (ControllerDependencyReflection.IsModuleInfrastructureAssembly(dependency.Assembly))
                return dependency.FullName;
        }

        return null;
    }

    /// <summary>
    /// Synthetic stand-in coupled to a real module-Infrastructure type (IdentityDbContext lives in
    /// AqtCoreFW.Identity.Infrastructure) — teeth test only.
    /// </summary>
    private sealed class FakeModuleInfraCoupledController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public FakeModuleInfraCoupledController(Identity.Infrastructure.Persistence.IdentityDbContext db) =>
            _ = db;
    }
}
