using AqtCoreFW.Identity.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authorization;

/// <summary>
/// Unit tests for MfaRequirementHandler (QĐ 742 1.6.b MFA step-up).
/// Tests mfa_at claim validation, age calculation, and failure scenarios.
/// Tests work through the AuthorizationHandler pattern (protected HandleRequirementAsync).
/// </summary>
public sealed class MfaRequirementHandlerTests
{
    private async Task<AuthorizationHandlerContext> ExecuteHandlerAsync(
        ClaimsPrincipal principal, MfaRequirement requirement)
    {
        var handler = new MfaRequirementHandler();
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            null);

        // Call through the public Handle method (which calls protected HandleRequirementAsync)
        await handler.HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task Handler_NoMfaAtClaim_Fails()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var requirement = new MfaRequirement(8);

        // Act
        var context = await ExecuteHandlerAsync(principal, requirement);

        // Assert
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_FreshMfaAt_Succeeds()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var epochSeconds = ((DateTimeOffset)now.AddHours(-1)).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("mfa_at", epochSeconds.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var requirement = new MfaRequirement(8);

        // Act
        var context = await ExecuteHandlerAsync(principal, requirement);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_StaleMfaAt_Fails()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var epochSeconds = ((DateTimeOffset)now.AddHours(-9)).ToUnixTimeSeconds(); // 9 hours old
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("mfa_at", epochSeconds.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var requirement = new MfaRequirement(8); // Max age = 8 hours

        // Act
        var context = await ExecuteHandlerAsync(principal, requirement);

        // Assert
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_BoundaryMfaAt_Succeeds()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var mfaTime = now.AddHours(-7).AddMinutes(-59); // 7h59m old
        var epochSeconds = ((DateTimeOffset)mfaTime).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("mfa_at", epochSeconds.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var requirement = new MfaRequirement(8);

        // Act
        var context = await ExecuteHandlerAsync(principal, requirement);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_InvalidMfaAtClaim_Fails()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("mfa_at", "not-a-number"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var requirement = new MfaRequirement(8);

        // Act
        var context = await ExecuteHandlerAsync(principal, requirement);

        // Assert
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_EmptyMfaAtClaim_Fails()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("mfa_at", string.Empty),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var requirement = new MfaRequirement(8);

        // Act
        var context = await ExecuteHandlerAsync(principal, requirement);

        // Assert
        context.HasFailed.Should().BeTrue();
    }
}
