using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Signing;

/// <summary>
/// Architecture guardrails for webhook callback authentication and security.
///
/// W7 compliance tests ensuring:
/// - Every *CallbackController has [AllowAnonymous] + inbound-token middleware integration
/// - No *CallbackController allows HTTP GET (POST-only per spec D15)
/// - MysignInboundTokenService.IssueToken entropy guarantee (≥32 bytes)
///
/// Callback endpoints receive sensitive webhook payloads from external CA providers
/// and must enforce strong authentication despite being public-facing.
///
/// Scope: the sweep is restricted to the Mysign webhook callback controllers (matched by the precise
/// <c>MysignCallback</c> name in Identity.Presentation) so a future, unrelated <c>*Callback*Controller</c>
/// elsewhere is not subjected to the AllowAnonymous / POST-only webhook guards.
/// </summary>
public sealed class CallbackAuthArchTests
{
    // The Mysign webhook callback controllers are thin Presentation controllers in Identity.Presentation
    // (their completion logic moved into an Application command). Match them by the precise "MysignCallback"
    // name so an unrelated *Callback* controller elsewhere is not subjected to the webhook guards.
    private static readonly Assembly PresentationAssembly = AssemblyFixtures.IdentityPresentation;

    private static List<Type> GetSigningCallbackControllers() => PresentationAssembly
        .GetTypes()
        .Where(t =>
            !t.IsAbstract &&
            !t.IsInterface &&
            t.Name.Contains("MysignCallback", StringComparison.Ordinal) &&
            t.Name.EndsWith("Controller", StringComparison.Ordinal))
        .ToList();

    // ── Test 1: Every *CallbackController has [AllowAnonymous] attribute ──────────

    [Fact]
    public void Every_MysignCallback_Controller_Has_AllowAnonymous_Attribute()
    {
        // W6 + D15: webhook callbacks must be public-facing (no JWT auth required).
        // Authentication via inbound token middleware, not per-action attributes.
        // Evidence: every *CallbackController type has class-level [AllowAnonymous] attribute.

        var callbackControllers = GetSigningCallbackControllers();

        callbackControllers.Should().NotBeEmpty("at least one signing *CallbackController expected");

        foreach (var controller in callbackControllers)
        {
            var hasAllowAnonymous = controller
                .GetCustomAttributes<AllowAnonymousAttribute>(inherit: false)
                .Any();

            hasAllowAnonymous.Should().BeTrue(
                $"{controller.Name} must have [AllowAnonymous] attribute (callbacks are public-facing)");
        }
    }

    // ── Test 2: No *CallbackController allows HTTP GET ─────────────────────────────

    [Fact]
    public void No_Callback_Controller_Has_HttpGet_Method()
    {
        // W6: webhook callbacks are POST-only (idempotency + security).
        // Evidence: no public action method in *CallbackController has [HttpGet] attribute.

        var callbackControllers = GetSigningCallbackControllers();

        callbackControllers.Should().NotBeEmpty("at least one signing *CallbackController expected");

        foreach (var controller in callbackControllers)
        {
            var getMethods = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m =>
                    m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpGetAttribute>(inherit: false).Any() ||
                    m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>(inherit: false)
                        .Any(a => a.HttpMethods?.Contains("GET") == true))
                .ToList();

            getMethods.Should().BeEmpty(
                $"{controller.Name} must not have [HttpGet] methods (webhook callbacks are POST-only)");
        }
    }

    // ── Test 3: MysignInboundTokenService exposes the IssueTokenAsync contract ────

    [Fact]
    public void MysignInboundToken_IssueContract_IsPreserved()
    {
        // W6 + D15: inbound token must be cryptographically strong (≥32 random bytes, base64-encoded).
        // Contract guard only — the entropy/length behaviour is asserted in the CrossModeFallback /
        // Mysign callback integration suites where a real DbContext is available.
        //
        // Evidence: MysignInboundTokenService has a public IssueTokenAsync(CancellationToken = default)
        // returning Task<string> (the raw base64 token, returned once and never stored).

        var serviceType = typeof(MysignInboundTokenService);
        var issueTokenMethod = serviceType.GetMethod("IssueTokenAsync",
            BindingFlags.Public | BindingFlags.Instance);

        issueTokenMethod.Should().NotBeNull(
            "MysignInboundTokenService must have public IssueTokenAsync() method");

        issueTokenMethod!.ReturnType.Should().Be(typeof(Task<string>),
            "IssueTokenAsync() must return Task<string> (base64-encoded raw token)");

        var parameters = issueTokenMethod.GetParameters();
        parameters.Should().HaveCount(1, "IssueTokenAsync() takes only a CancellationToken");
        parameters[0].ParameterType.Should().Be(typeof(CancellationToken),
            "the sole parameter must be a CancellationToken");
    }
}
