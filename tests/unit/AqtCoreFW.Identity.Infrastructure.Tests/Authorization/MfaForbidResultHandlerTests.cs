using AqtCoreFW.Identity.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Security.Claims;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authorization;

/// <summary>
/// Unit tests for <see cref="MfaForbidResultHandler"/> — the X-Mfa-Required hint that
/// drives the SPA's redirect to the 2FA step-up screen.
///
/// The load-bearing invariant: the hint is emitted ONLY when MFA step-up is the genuine
/// unmet condition. An endpoint can carry BOTH [RequiresMfa] and a permission policy; a
/// permission-caused 403 there must NOT be mislabelled as an MFA requirement, otherwise a
/// user who simply lacks the permission gets bounced to the 2FA page (a dead-end for roles
/// where MFA is not even mandatory).
/// </summary>
public sealed class MfaForbidResultHandlerTests
{
    private const string MfaRequiredHeader = "X-Mfa-Required";

    private static ClaimsPrincipal AuthenticatedUser(string? mfaAtEpochSeconds)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (mfaAtEpochSeconds is not null)
        {
            claims.Add(new Claim("mfa_at", mfaAtEpochSeconds));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
    }

    private static string EpochHoursAgo(double hours)
        => ((DateTimeOffset)DateTime.UtcNow.AddHours(-hours)).ToUnixTimeSeconds().ToString();

    private static async Task<HttpContext> RunAsync(
        AuthorizationPolicy policy, PolicyAuthorizationResult result, ClaimsPrincipal user)
    {
        // The default handler delegates a Forbidden result to context.ForbidAsync(), which
        // resolves IAuthenticationService from RequestServices — substitute is enough.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IAuthenticationService>());

        var ctx = new DefaultHttpContext
        {
            User = user,
            RequestServices = services.BuildServiceProvider(),
        };

        await new MfaForbidResultHandler().HandleAsync(_ => Task.CompletedTask, ctx, policy, result);
        return ctx;
    }

    private static AuthorizationPolicy PolicyWithMfa(int maxAgeHours) =>
        new AuthorizationPolicyBuilder()
            .AddRequirements(new MfaRequirement(maxAgeHours))
            .RequireAuthenticatedUser()
            .Build();

    private static AuthorizationPolicy PolicyWithoutMfa() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

    [Fact]
    public async Task Forbidden_PermissionDeniedButMfaFresh_DoesNotEmitHeader()
    {
        // The regression guard: 403 on an [RequiresMfa] endpoint denied by the *permission*
        // (MFA itself is satisfied) must surface as a plain forbidden, not a 2FA redirect.
        var ctx = await RunAsync(
            PolicyWithMfa(8),
            PolicyAuthorizationResult.Forbid(),
            AuthenticatedUser(EpochHoursAgo(1)));

        ctx.Response.Headers.ContainsKey(MfaRequiredHeader).Should().BeFalse();
    }

    [Fact]
    public async Task Forbidden_StaleMfa_EmitsHeader()
    {
        var ctx = await RunAsync(
            PolicyWithMfa(8),
            PolicyAuthorizationResult.Forbid(),
            AuthenticatedUser(EpochHoursAgo(9)));

        ctx.Response.Headers.ContainsKey(MfaRequiredHeader).Should().BeTrue();
    }

    [Fact]
    public async Task Forbidden_MissingMfaClaim_EmitsHeader()
    {
        var ctx = await RunAsync(
            PolicyWithMfa(8),
            PolicyAuthorizationResult.Forbid(),
            AuthenticatedUser(mfaAtEpochSeconds: null));

        ctx.Response.Headers.ContainsKey(MfaRequiredHeader).Should().BeTrue();
    }

    [Fact]
    public async Task Forbidden_PolicyWithoutMfaRequirement_DoesNotEmitHeader()
    {
        var ctx = await RunAsync(
            PolicyWithoutMfa(),
            PolicyAuthorizationResult.Forbid(),
            AuthenticatedUser(EpochHoursAgo(9)));

        ctx.Response.Headers.ContainsKey(MfaRequiredHeader).Should().BeFalse();
    }

    [Fact]
    public async Task Forbidden_UnauthenticatedUser_DoesNotEmitHeader()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type → not authenticated

        var ctx = await RunAsync(PolicyWithMfa(8), PolicyAuthorizationResult.Forbid(), anonymous);

        ctx.Response.Headers.ContainsKey(MfaRequiredHeader).Should().BeFalse();
    }

    [Fact]
    public async Task NotForbidden_DoesNotEmitHeader()
    {
        var ctx = await RunAsync(
            PolicyWithMfa(8),
            PolicyAuthorizationResult.Success(),
            AuthenticatedUser(EpochHoursAgo(9)));

        ctx.Response.Headers.ContainsKey(MfaRequiredHeader).Should().BeFalse();
    }
}
