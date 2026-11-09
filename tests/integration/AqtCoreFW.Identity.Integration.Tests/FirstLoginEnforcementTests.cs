using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for FirstLoginPasswordChangeMiddleware (QĐ 742 1.3.a).
/// Tests MustChangePassword enforcement and whitelisted paths.
/// Runs against Testcontainers SQL Server via WebAppFixture.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FirstLoginEnforcementTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    // Instance fields so each test method (xUnit creates a new class instance per test)
    // gets its own user, preventing state leakage between tests.
    private readonly Guid _testTenantId = Guid.NewGuid();
    private readonly Guid _testUserId = Guid.NewGuid();

    public FirstLoginEnforcementTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>Seeds an ApplicationUser with MustChangePassword=true and MFA enrollment (for Phase-03 [RequiresMfa] attribute).</summary>
    private async Task SeedUserWithMustChangePasswordAsync(bool mustChange = true)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await userManager.FindByIdAsync(_testUserId.ToString());
        if (existing != null) return;

        var user = new ApplicationUser
        {
            Id = _testUserId,
            UserName = $"testpwdchange-{_testUserId:N}",
            Email = $"testpwdchange-{_testUserId:N}@test.vn",
            EmailConfirmed = true,
            FullName = "Password Change Test User",
            TenantId = _testTenantId,
            IsActive = true,
            MustChangePassword = mustChange,
            // Phase-03: Enroll in MFA to bypass [RequiresMfa] gate on admin endpoints (credential-policies)
            TotpSecret = "JBSWY3DPEBLW64TMMQXDOMPQ", // Base32-encoded dummy secret for testing
            TotpEnrolledAt = DateTime.UtcNow,
            RecoveryCodesHash = "[]", // Empty recovery codes (not tested here)
        };
        var result = await userManager.CreateAsync(user, "TempPass@12345!");
        result.Succeeded.Should().BeTrue($"Failed to seed user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    [Fact]
    public async Task AuthenticatedRequest_WithMustChangePassword_ReturnsMiddlewareBlock()
    {
        // Arrange
        await SeedUserWithMustChangePasswordAsync(mustChange: true);
        // SystemAdmin role so authz does not short-circuit before middleware runs.
        // Middleware (now BEFORE UseAuthorization) must intercept first regardless.
        // Phase-03: Pass includeMfaClaim=true to bypass [RequiresMfa] attribute so we test middleware, not MFA.
        var client = _app.CreateAuthenticatedClient(_testUserId, _testTenantId, includeMfaClaim: true, "SystemAdmin");

        // Act: Access a protected endpoint that is NOT whitelisted
        var response = await client.GetAsync("/api/v1/admin/credential-policies");

        // Assert: Should be blocked by FirstLoginPasswordChangeMiddleware with 403 + header
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Should().ContainKey("X-Must-Change-Password");
    }

    [Fact]
    public async Task ChangePassword_Endpoint_BypassesMiddleware()
    {
        // Arrange
        await SeedUserWithMustChangePasswordAsync(mustChange: true);
        var client = _app.CreateAuthenticatedClient(_testUserId, _testTenantId);

        // Act
        var changeRequest = new
        {
            currentPassword = "TempPass@12345!",
            newPassword = "NewPass@12345!",
        };
        var response = await client.PostAsJsonAsync("/api/v1/account/change-password", changeRequest);

        // Assert
        // /api/v1/account/change-password is whitelisted by middleware
        // Should NOT be blocked with 403 (middleware allows it)
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Health_Endpoint_BypassesMiddleware()
    {
        // Arrange
        await SeedUserWithMustChangePasswordAsync(mustChange: true);
        var client = _app.CreateAuthenticatedClient(_testUserId, _testTenantId);

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        // /health is whitelisted by middleware
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnauthenticatedRequest_BypassesMustChangePasswordCheck()
    {
        // Arrange
        var client = _app.CreateClient(); // Not authenticated

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        // Unauthenticated requests bypass middleware entirely
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserWithoutMustChangeFlag_BypassesMiddlewareGate()
    {
        // Arrange
        await SeedUserWithMustChangePasswordAsync(mustChange: false);
        // SystemAdmin so authorization passes — this test proves middleware does NOT block
        // when MustChangePassword == false. Without SystemAdmin the endpoint returns 403 from
        // authorization, which would be a false pass of the middleware check.
        // Phase-03: Pass includeMfaClaim=true to bypass [RequiresMfa] attribute on credential-policies endpoint.
        var client = _app.CreateAuthenticatedClient(_testUserId, _testTenantId, includeMfaClaim: true, "SystemAdmin");

        // Act
        var response = await client.GetAsync("/api/v1/admin/credential-policies");

        // Assert
        // User without flag should NOT be blocked by middleware.
        // With SystemAdmin + no MustChangePassword flag → 200 OK (or non-403).
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
