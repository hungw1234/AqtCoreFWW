using System.Security.Claims;
using AqtCoreFW.Infrastructure.BackgroundJobs;
using FluentAssertions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.BackgroundJobs;

/// <summary>
/// Unit tests for the Hangfire dashboard authorization predicate.
///
/// The dashboard is a security control: it authorizes ONLY a SystemAdmin, in EVERY environment
/// (no dev bypass). These pin the role-only contract — the predicate takes no environment input,
/// so there is no path by which an anonymous or non-admin caller is authorized.
/// </summary>
public sealed class HangfireAuthorizationFilterTests
{
    private static ClaimsPrincipal AuthenticatedWithRoles(params string[] roles) =>
        // authenticationType set → IsAuthenticated true; role claims drive IsInRole.
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)),
            authenticationType: "TestAuth"));

    [Fact]
    public void IsAuthorized_SystemAdmin_ReturnsTrue() =>
        HangfireAuthorizationFilter.IsAuthorized(AuthenticatedWithRoles("SystemAdmin")).Should().BeTrue();

    [Fact]
    public void IsAuthorized_NonAdminAuthenticated_ReturnsFalse() =>
        HangfireAuthorizationFilter.IsAuthorized(AuthenticatedWithRoles("Admin", "GovOfficer")).Should().BeFalse();

    [Fact]
    public void IsAuthorized_AnonymousPrincipal_ReturnsFalse() =>
        HangfireAuthorizationFilter.IsAuthorized(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeFalse();

    [Fact]
    public void IsAuthorized_Null_ReturnsFalse() =>
        HangfireAuthorizationFilter.IsAuthorized(null).Should().BeFalse();
}
