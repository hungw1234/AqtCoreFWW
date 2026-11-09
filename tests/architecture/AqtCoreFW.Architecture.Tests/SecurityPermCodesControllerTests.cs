using System.Reflection;
using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// CI guard for plan 260523-1241 Phase 06a — `/api/v1/security/perm-codes` endpoint.
///
/// Workflow designer's ByPermission clause editor needs the full PermCodes.All list
/// (~129 codes) at runtime. Endpoint is intentionally NOT under /admin/ (PermCode
/// strings are public identifiers, not credentials) but DOES require authentication
/// to avoid anonymous enumeration of the policy namespace.
///
/// Reflection-based so test compiles even before controller exists (true TDD red bar).
/// </summary>
public sealed class SecurityPermCodesControllerTests
{
    private static Type ResolveController() =>
        AssemblyFixtures.ApiHost.GetType("AqtCoreFW.Api.Controllers.SecurityController")
            ?? throw new InvalidOperationException(
                "SecurityController not found in AqtCoreFW.Api host assembly. " +
                "Expected at AqtCoreFW.Api.Controllers.SecurityController per plan 260523-1241 Phase 06a.");

    [Fact]
    public void SecurityController_Exists_InApiHostAssembly()
    {
        var t = AssemblyFixtures.ApiHost.GetType("AqtCoreFW.Api.Controllers.SecurityController");
        t.Should().NotBeNull("plan 260523-1241 Phase 06a ships /api/v1/security/perm-codes endpoint");
    }

    [Fact]
    public void SecurityController_HasRouteApiV1SecurityPermCodes()
    {
        var t = ResolveController();
        var route = t.GetCustomAttribute<RouteAttribute>();
        route.Should().NotBeNull("class-level [Route] is required for orval discovery");
        route!.Template.Should().Be("api/v1/security/perm-codes",
            "route is fixed by Phase 06 plan + Q4 ratification");
    }

    [Fact]
    public void SecurityController_RequiresAuthenticatedUser_NotAnonymous()
    {
        var t = ResolveController();
        var auth = t.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull(
            "endpoint must reject anonymous to avoid enumerating policy namespace");
        // Authenticated-only (no specific Policy) — PermCode strings are public identifiers,
        // not secrets; narrowing to a single permission would lock out role-permissions-drawer reuse.
        auth!.Policy.Should().BeNull(
            "no specific policy gate — any authenticated user may read the public PermCode list");
    }

    [Fact]
    public void SecurityController_HasGetMethodReturningStringList()
    {
        var t = ResolveController();
        var method = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.GetCustomAttribute<HttpGetAttribute>() is not null);
        method.Should().NotBeNull("at least one [HttpGet] action is required");

        var produces = method!.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        produces.Should().Contain(a => a.StatusCode == 200,
            "[ProducesResponseType(200)] required for orval response-type generation");
    }
}
