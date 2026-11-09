using System.Reflection;
using System.Text.RegularExpressions;
using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Contract guard for decentralized authz: every permission-code CHECKED at a resource — via
/// <c>[Authorize(Policy = code)]</c> on any controller (class- or action-level) across every host —
/// MUST be a grantable code (present in <see cref="PermCodes.All"/>).
///
/// Why <c>PermCodes.All</c> is the reference set: the existing PermCodesSeederCoverageTests binds
/// <c>PermCodes.All ⇔ seeded-catalog</c> bidirectionally, so <c>checked ⊆ All</c> transitively proves
/// <c>checked ⊆ grantable</c> without re-reading the (internal) seeder here — one source of truth, DRY.
///
/// What it catches that nothing else does: a raw-string policy that bypasses the typed <c>PermCodes.*</c>
/// constants (e.g. <c>[Authorize(Policy = "FORMS.SUBMIT.TYPO")]</c>). Such a policy is never registered
/// (policies are bulk-registered from <c>PermCodes.All</c>) → fail-closed 403 / 500 at runtime, invisible
/// at compile time because the dynamic <c>MfaPolicyProvider</c> delegates unknown names to the default
/// provider. Today every call-site uses the constants, so this guard is a green regression lock — the
/// <see cref="Scanner_Flags_A_Synthetic_NonGrantable_Code"/> self-test proves it is not vacuously green.
///
/// Non-PermCode policies (ABAC, OR-aggregate, dynamic MFA) are intentionally out of scope — their names
/// are not MODULE.RESOURCE.ACTION shaped, so <see cref="PermCodeShape"/> excludes them; the explicit
/// allow-list documents intent for the same-shaped edge cases (there are none today).
/// </summary>
public sealed class PermCodeCheckedGrantableContractTests
{
    // MODULE.RESOURCE.ACTION — uppercase tokens (letters/digits/underscore), exactly two dots.
    private static readonly Regex PermCodeShape =
        new("^[A-Z][A-Z0-9_]*(\\.[A-Z0-9_]+){2}$", RegexOptions.Compiled);

    // Policy names that are legitimately NOT permission codes (registered via ABAC handlers, an
    // OR-aggregate requirement, or the dynamic MFA provider). None are PermCode-shaped, so this is
    // defensive documentation rather than a functional filter.
    private static readonly HashSet<string> KnownNonPermCodePolicies = new(StringComparer.Ordinal)
    {
        Policies.DepartmentRestricted,
        Policies.ClassifiedAccess,
        Policies.SignatureDocHashAccess,
    };

    /// <summary>Reflection over a controller-hosting assembly can hit unresolved types; keep the loaded ones.</summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    /// <summary>All policy names referenced by [Authorize] on any controller (class- or action-level).</summary>
    private static IEnumerable<string> CollectCheckedPolicies()
    {
        foreach (var assembly in AssemblyFixtures.PresentationAssemblies)
        foreach (var controller in SafeGetTypes(assembly)
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var attr in controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                if (!string.IsNullOrWhiteSpace(attr.Policy))
                    yield return attr.Policy!;

            // Include inherited methods (no DeclaredOnly): an action + [Authorize(Policy=…)] declared on an
            // abstract base controller and inherited by a concrete one would otherwise escape the sweep.
            // Framework methods carry no policy (filtered below) and duplicate policies are de-duped by the caller.
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            foreach (var attr in method.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                if (!string.IsNullOrWhiteSpace(attr.Policy))
                    yield return attr.Policy!;
        }
    }

    [Fact]
    public void Every_Checked_PermCode_Policy_Must_Be_Grantable()
    {
        var grantable = PermCodes.All.ToHashSet(StringComparer.Ordinal);

        var offenders = CollectCheckedPolicies()
            .Where(policy => PermCodeShape.IsMatch(policy) && !KnownNonPermCodePolicies.Contains(policy))
            .Where(policy => !grantable.Contains(policy))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "every PermCode-shaped policy referenced by [Authorize(Policy = …)] must exist in PermCodes.All — " +
            "an unlisted code is never registered as a policy nor granted, so the endpoint fails closed at runtime. " +
            "Use a PermCodes.* constant and add it to the catalog. Non-grantable checked codes: {0}",
            string.Join(", ", offenders));
    }

    [Fact]
    public void Scanner_Actually_Sees_Real_PermCode_Policies()
    {
        // Guards against a silently-empty sweep (e.g. a fixture change that stops loading controllers):
        // the contract test is only meaningful if it actually observes PermCode-shaped checks.
        var checkedPermCodes = CollectCheckedPolicies()
            .Where(policy => PermCodeShape.IsMatch(policy))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        checkedPermCodes.Should().NotBeEmpty(
            "expected controllers across the host assemblies to reference PermCode policies via [Authorize]; " +
            "an empty result means the reflection sweep is broken, not that no endpoints are protected");
    }

    [Fact]
    public void Scanner_Flags_A_Synthetic_NonGrantable_Code()
    {
        // Anti-false-green: prove the shape filter + grantable check would reject a fabricated code.
        var grantable = PermCodes.All.ToHashSet(StringComparer.Ordinal);
        const string fake = "FORMS.SUBMIT.FAKEACTION";

        PermCodeShape.IsMatch(fake).Should().BeTrue("the fake must be PermCode-shaped so the real filter considers it");
        grantable.Contains(fake).Should().BeFalse("the fake must be absent from the catalog so the real assertion flags it");
    }
}
