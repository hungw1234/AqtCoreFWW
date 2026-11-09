using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Signing;

/// <summary>
/// Guardrail: the front-end-facing signing-request controller must be routed under the
/// versioned <c>api/v1</c> prefix.
///
/// The SPA's apiClient has <c>baseURL = "/api/v1"</c> and calls the relative path
/// <c>/signing/request</c>, so the wire URL is <c>/api/v1/signing/request</c>. If the controller
/// is mounted at an un-versioned <c>api/signing/request</c>, every front-end signing call 404s —
/// and the failure is invisible to component tests that mock the api function (they assert the
/// relative path, never the resolved route). This test pins the contract at the route level so the
/// mismatch fails here instead of only at runtime.
///
/// The machine-to-machine callback controllers (doc-flow file handler, mysign callbacks) are
/// intentionally NOT covered: they are reached via server-minted URLs, not the SPA apiClient, so
/// they live at <c>api/signing/...</c> by design.
/// </summary>
public sealed class SigningRequestRouteConventionArchTests
{
    // The SPA-facing signing-request controller lives in Identity.Presentation (it dispatches an
    // Identity Application command); its route contract is unchanged by that relocation.
    private static readonly Assembly PresentationAssembly = AssemblyFixtures.IdentityPresentation;

    [Fact]
    public void SigningRequestController_Is_Routed_Under_ApiV1()
    {
        var controller = PresentationAssembly.GetType(
            "AqtCoreFW.Identity.Presentation.Controllers.SigningRequestController");

        controller.Should().NotBeNull(
            "the provider-agnostic signing entry-point controller must exist");

        var routeTemplate = controller!
            .GetCustomAttribute<RouteAttribute>(inherit: false)?
            .Template;

        routeTemplate.Should().NotBeNull("SigningRequestController must declare a [Route] template");

        routeTemplate!.Should().StartWith("api/v1/",
            "this is a front-end-facing endpoint and the SPA apiClient prepends baseURL '/api/v1' " +
            "to the relative path it calls; an un-versioned route 404s every signing request");

        routeTemplate.Should().Be("api/v1/signing/request",
            "the SPA calls the relative path '/signing/request' which resolves to /api/v1/signing/request");
    }
}
