using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Reusable architecture-guard helpers for ASP.NET Core host smoke tests.
///
/// Purpose: detect transitive DI registration gaps that <c>ValidateOnBuild=true</c>
/// would miss. Controllers are NOT registered in the DI container by default — they
/// are constructed per-request via <see cref="ActivatorUtilities.CreateInstance"/>,
/// so DI failures only surface on first HTTP call. This helper enumerates every
/// public controller across the host AND every <see cref="ApplicationPart"/> wired
/// via <c>AddApplicationPart</c>, then resolves each eagerly inside a scope.
///
/// Incident precedents that motivated this guard:
///   * v2.133p3 (2026-04-28): IMessageBus missing → AuthServer /connect/authorize 500
///   * 2026-05-08:           IGuidGenerator missing → PHASE7-TIER-BC-VALIDATION miss
///
/// Used by both <c>ApiControllerDiResolutionSmokeTests</c> and
/// <c>AuthServerStartupValidationTests</c>. Extract trigger: 2 callers (Api + AuthServer
/// hosts). When a 3rd host arrives (e.g. Gateway), promote to <c>tests/shared/</c>.
/// </summary>
public static class HostDiArchGuard
{
    /// <summary>
    /// Enumerates every controller discoverable via the running host's
    /// <see cref="ApplicationPartManager"/> + <see cref="ControllerFeatureProvider"/>.
    ///
    /// This is the canonical ASP.NET Core mechanism for controller discovery — it
    /// returns the exact set MVC routes against, including:
    ///   * Host-local controllers (host assembly auto-registered as ApplicationPart)
    ///   * Module controllers added via <c>AddApplicationPart(typeof(X).Assembly)</c>
    ///
    /// AUTHSERVER-DI-ARCH-FU4 (2026-05-09): Replaces assembly-only enumeration so module
    /// Presentation assemblies (Identity/Forms/Workflow/etc., ~14 in Api host) are
    /// directly DI-validated rather than only transitively via host-local controllers.
    /// </summary>
    public static IReadOnlyList<Type> EnumerateAllControllers(IServiceProvider serviceProvider)
    {
        var partManager = serviceProvider.GetRequiredService<ApplicationPartManager>();
        var feature = new ControllerFeature();
        partManager.PopulateFeature(feature);
        return feature.Controllers.Cast<Type>().ToList();
    }

    /// <summary>
    /// For each controller in <paramref name="controllerTypes"/>, attempts construction via
    /// <see cref="ActivatorUtilities.CreateInstance"/> against the supplied scope's service
    /// provider. Returns the list of failure descriptions
    /// (<c>{TypeFullName}: {ExceptionType}: {Message}</c>) — empty on success.
    /// Caller asserts on the returned list (typically <c>Should().BeEmpty(...)</c>).
    /// </summary>
    public static IReadOnlyList<string> TryResolveControllers(
        IServiceProvider serviceProvider,
        IReadOnlyList<Type> controllerTypes)
    {
        var failures = new List<string>();

        foreach (var type in controllerTypes)
        {
            try
            {
                ActivatorUtilities.CreateInstance(serviceProvider, type);
            }
            catch (Exception ex)
            {
                var root = ex.GetBaseException();
                failures.Add($"{type.FullName}: {root.GetType().Name}: {root.Message}");
            }
        }

        return failures;
    }
}
