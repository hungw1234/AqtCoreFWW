using System.Reflection;
using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// CI guard for QÄ 742 1.6.b (Phase-03 Slice B).
///
/// Every admin controller across all controller-hosting assemblies (module Presentation
/// layers AND host assemblies AqtCoreFW.Api / AqtCoreFW.AuthServer) MUST carry
/// <see cref="RequiresMfaAttribute"/> at class level so that admin operations
/// are gated by a valid MFA session (mfa_at claim, max-age 8 h).
///
/// Failure mode: a new admin controller without [RequiresMfa] would allow
/// password-only sessions to perform privileged admin operations, violating
/// QÄ 742 multi-factor enforcement requirements.
///
/// Allowlist: controllers that are intentionally MFA-exempt must be explicitly
/// added to the allowlist below with a justification comment.
/// </summary>
public sealed class AdminControllersMfaAttributeTests
{
    /// <summary>
    /// Controllers intentionally exempt from [RequiresMfa] â€” must be explicitly allowlisted with justification.
    /// Key = FullName, Value = reason.
    /// </summary>
    private static readonly Dictionary<string, string> MfaExemptControllers = new()
    {
        // Account-level self-service: user starts enrolment from a session without an active
        // mfa_at claim. AccountMfaController gates on [Authorize] + admin approvals via separate flow.
        ["AqtCoreFW.Identity.Presentation.Controllers.AccountMfaController"] =
            "Self-service enrolment endpoints - user has no MFA session yet by definition.",
    };

    [Fact]
    public void AllAdminControllers_HaveRequiresMfaAttribute()
    {
        // Sweep all controller-hosting assemblies â€” module Presentation layers + Api/AuthServer hosts.
        var adminControllers = AssemblyFixtures.PresentationAssemblies
            .SelectMany(SafeGetTypes)
            .Where(IsAdminController)
            .ToList();

        var missing = adminControllers
            .Where(t => !MfaExemptControllers.ContainsKey(t.FullName ?? t.Name))
            .Where(t => !HasRequiresMfaAtClassLevel(t))
            .Select(t => $"{t.Assembly.GetName().Name}: {t.FullName}")
            .OrderBy(s => s)
            .ToList();

        missing.Should().BeEmpty(
            because: "all admin controllers must carry [RequiresMfa] at class level (QÄ 742 1.6.b Phase-03). " +
                     "Add [RequiresMfa] to the failing controller(s) or add to MfaExemptControllers with justification.");
    }

    [Fact]
    public void AdminControllersCount_AtLeastExpectedMinimum()
    {
        // Guard against the allowlist swallowing all controllers.
        // Floor raised to 12 to cover module Presentation + host (Api) admin controllers.
        var adminControllers = AssemblyFixtures.PresentationAssemblies
            .SelectMany(SafeGetTypes)
            .Where(IsAdminController)
            .ToList();

        adminControllers.Count.Should().BeGreaterThanOrEqualTo(12,
            because: "at least 12 admin controllers were swept in Phase-03 (module Presentation + Api host). " +
                     "If this fails, check whether controllers were deleted or renamed without updating this test.");
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static bool IsAdminController(Type t)
    {
        if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) return false;
        if (!typeof(ControllerBase).IsAssignableFrom(t)) return false;

        // Convention: admin controllers end with "AdminController" or are named *Admin*Controller
        return t.Name.EndsWith("AdminController", StringComparison.Ordinal)
            || t.Name.EndsWith("RulesAdminController", StringComparison.Ordinal);
    }

    private static bool HasRequiresMfaAtClassLevel(Type t)
    {
        // Check for [RequiresMfa] declared directly on the class (not inherited from base)
        return t.GetCustomAttributes(inherit: false)
                .Any(a => a.GetType().Name == nameof(RequiresMfaAttribute));
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
