using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.Architecture.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guard G3 (ADR-011 rule 3 — "host Api = pure composition root").
///
/// A controller in <c>AqtCoreFW.Api</c> whose constructor-injected dependencies belong to EXACTLY ONE
/// module (<c>AqtCoreFW.&lt;Module&gt;.*</c>, excluding SharedKernel + the shared infra assembly) is a
/// single-module business controller that belongs in that module's <c>*.Presentation</c> layer, not in
/// the host. Footprint 0 (only SharedKernel / shared infra) = a legitimate host resident (admin surface
/// over shared infra, <c>SecurityController</c>, <c>ISender</c>-only dispatch). Footprint ≥ 2 = a
/// genuine cross-module host concern (G1 covers any Infrastructure coupling it carries).
///
/// G3 is redundant with G1+G2 for raw correctness but encodes the placement invariant the remediation
/// exists to establish, so it stays — paired with positive tests proving it does NOT false-flag the
/// permanent shared-infra <c>Admin/*</c> residents.
///
/// Footprint is computed from CONSTRUCTOR injection only: a host controller that injects just
/// <c>ISender</c> and dispatches a module command via MediatR is correctly footprint 0 (it is not
/// structurally coupled to the module). That is the boundary between "thin host dispatcher" and
/// "single-module controller misplaced in the host".
/// </summary>
public sealed class HostContainsNoSingleModuleControllerGuardTests
{
    /// <summary>
    /// Single-module controllers still living in the host. Key = FullName, Value = reason + removing
    /// phase. Shrinks to EMPTY as each controller relocates to its module's Presentation layer; the
    /// empty end-state is asserted in Phase-6 closure.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownSingleModuleHostControllers =
        new Dictionary<string, string>(); // empty — all single-module host controllers relocated to their module Presentation layer

    [Fact]
    public void NoHostController_IsCoupledToExactlyOneModule()
    {
        var violations = AssemblyFixtures.PresentationAssemblies // ApiHost is the only one scanned below
            .Where(a => a == AssemblyFixtures.ApiHost)
            .SelectMany(ControllerDependencyReflection.DiscoverControllers)
            .Where(c => !KnownSingleModuleHostControllers.ContainsKey(c.FullName ?? c.Name))
            .Where(IsSingleModuleController)
            .Select(c => $"{c.FullName} → module {ControllerDependencyReflection.ModuleFootprint(c).Single()}")
            .OrderBy(s => s)
            .ToList();

        violations.Should().BeEmpty(
            "a single-module controller belongs in that module's *.Presentation layer, not in the host " +
            "composition root. Misplaced host controllers: {0}",
            string.Join("; ", violations));
    }

    [Theory]
    [MemberData(nameof(HostAdminControllerNames))]
    public void HostAdminController_IsNotFlagged(string controllerFullName)
    {
        // Positive tests (V3): each permanent shared-infra Admin/* resident must NOT be flagged — its
        // footprint is 0 (shared AqtCoreFW.Infrastructure + SharedKernel only), never exactly 1 module.
        // This proves the footprint heuristic does not false-positive the controllers that legitimately
        // stay in the host, which is the whole risk of keeping G3.
        var controller = ControllerDependencyReflection.DiscoverControllers(AssemblyFixtures.ApiHost)
            .Single(c => c.FullName == controllerFullName);

        IsSingleModuleController(controller).Should().BeFalse(
            "{0} is a shared-infra admin controller and must have footprint 0, not 1 module. " +
            "Actual footprint: [{1}]",
            controllerFullName,
            string.Join(", ", ControllerDependencyReflection.ModuleFootprint(controller)));
    }

    [Fact]
    public void Allowlist_EntriesAreStillRealHostViolations()
    {
        // Tripwire: each allowlisted controller must still reside in the host AND still be coupled to
        // exactly one module. A relocated controller (FullName changes with its namespace) or one whose
        // coupling was refactored away leaves a stale entry that fails here, forcing same-commit cleanup —
        // and keeps footprint-0 ISender dispatchers (which are NOT violations) out of the allowlist.
        var hostControllers = ControllerDependencyReflection.DiscoverControllers(AssemblyFixtures.ApiHost)
            .GroupBy(t => t.FullName)
            .ToDictionary(g => g.Key!, g => g.First());

        var stale = KnownSingleModuleHostControllers.Keys
            .Where(name => !hostControllers.TryGetValue(name, out var controller) ||
                           !IsSingleModuleController(controller))
            .ToList();

        stale.Should().BeEmpty(
            "an allowlist entry must name a host controller that still exists AND is still coupled to " +
            "exactly one module. Stale entries (relocated/refactored/not-a-violation): {0}",
            string.Join(", ", stale));
    }

    [Fact]
    public void Closure_AllowlistIsEmpty()
    {
        // Every single-module controller was relocated to its module's Presentation layer. Pin the empty
        // end-state so a future single-module controller landing in the host fails here.
        KnownSingleModuleHostControllers.Should().BeEmpty(
            "the host composition root must contain no single-module business controller");
    }

    [Fact]
    public void Discovery_FindsHostControllers()
    {
        // Anti-vacuous: a broken base-type check would discover zero host controllers and pass the
        // violation test trivially.
        ControllerDependencyReflection.DiscoverControllers(AssemblyFixtures.ApiHost)
            .Should().HaveCountGreaterThanOrEqualTo(10,
                "the host exposes well over 10 controllers (13 Admin + signing + account)");
    }

    [Fact]
    public void Guard_FlagsASyntheticSingleModuleHostController_AndIgnoresZeroFootprint()
    {
        // Teeth proof: the SAME classifier flags a synthetic single-module controller and ignores a
        // zero-footprint (shared-infra/SharedKernel) one — a refactor neutering the footprint logic
        // fails HERE, not only via manual allowlist removal.
        IsSingleModuleController(typeof(FakeSingleModuleHostController)).Should().BeTrue(
            "a controller injecting exactly one module's Application type must be flagged");
        IsSingleModuleController(typeof(FakeSharedInfraHostController)).Should().BeFalse(
            "a controller injecting only an ISender-like SharedKernel dependency must not be flagged");
    }

    public static IEnumerable<object[]> HostAdminControllerNames() =>
        ControllerDependencyReflection.DiscoverControllers(AssemblyFixtures.ApiHost)
            .Where(c => (c.Name).EndsWith("AdminController", StringComparison.Ordinal))
            .Select(c => new object[] { c.FullName! });

    private static bool IsSingleModuleController(Type controller) =>
        ControllerDependencyReflection.ModuleFootprint(controller).Count == 1;

    // ── Synthetic fixtures (teeth test only) ─────────────────────────────────────

    private sealed class FakeSingleModuleHostController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public FakeSingleModuleHostController(Identity.Application.Services.IMfaService svc) => _ = svc;
    }

    private sealed class FakeSharedInfraHostController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public FakeSharedInfraHostController(MediatR.ISender sender) => _ = sender;
    }
}
