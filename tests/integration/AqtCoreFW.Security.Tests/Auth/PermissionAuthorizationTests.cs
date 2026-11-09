using System.Security.Claims;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;

namespace AqtCoreFW.Security.Tests.Auth;

/// <summary>
/// RBAC permission authorization tests â€” TC-SEC-AUTHZ-001 to 003.
/// Tests PermissionAuthorizationHandler with mock IEffectivePermissionService.
/// </summary>
public sealed class PermissionAuthorizationTests
{
    private static ClaimsPrincipal CreateUser(Guid userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static AuthorizationHandlerContext CreateContext(
        ClaimsPrincipal user, string requiredPermission)
    {
        var requirement = new PermissionRequirement(requiredPermission);
        return new AuthorizationHandlerContext([requirement], user, null);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UserWithPermission_Succeeds()
    {
        var userId = Guid.NewGuid();
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { "USERS.LIST.VIEW", "CASES.LIST.VIEW" }
            });

        var handler = new PermissionAuthorizationHandler(permService);
        var user = CreateUser(userId);
        var ctx = CreateContext(user, "USERS.LIST.VIEW");

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UserWithoutPermission_Denied()
    {
        var userId = Guid.NewGuid();
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { "CASES.LIST.VIEW" }
            });

        var handler = new PermissionAuthorizationHandler(permService);
        var user = CreateUser(userId);
        var ctx = CreateContext(user, "USERS.LIST.VIEW");

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnauthenticatedUser_Denied()
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        var handler = new PermissionAuthorizationHandler(permService);

        // No NameIdentifier claim
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var ctx = CreateContext(user, "USERS.LIST.VIEW");

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
        await permService.DidNotReceive().GetSummaryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task InvalidUserId_Denied()
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        var handler = new PermissionAuthorizationHandler(permService);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-guid") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var ctx = CreateContext(user, "USERS.LIST.VIEW");

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SubClaim_AlsoRecognized()
    {
        var userId = Guid.NewGuid();
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { "ADMIN.ALL" }
            });

        var handler = new PermissionAuthorizationHandler(permService);

        // Use "sub" claim instead of NameIdentifier
        var claims = new[] { new Claim("sub", userId.ToString()) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var ctx = CreateContext(user, "ADMIN.ALL");

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }
}
