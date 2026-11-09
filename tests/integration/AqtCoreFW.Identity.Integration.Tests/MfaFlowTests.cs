using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for the AccountMfaController (Phase-03 QĐ742 1.6.a).
/// Routes: /api/v1/account/mfa/* (Option B — replaces deleted /api/v1/mfa/* from MfaController).
///
/// Tests cover:
///   GET  /api/v1/account/mfa/enrol/totp     — begin TOTP enrolment
///   POST /api/v1/account/mfa/enrol/totp     — complete TOTP enrolment + recovery codes
///   DELETE /api/v1/account/mfa/totp         — unenrol (requires [RequiresMfa])
///   POST /api/v1/account/mfa/recovery-codes/regenerate — regenerate codes (requires [RequiresMfa])
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class MfaFlowTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public MfaFlowTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>Seeds an ApplicationUser into Identity DB so the MFA endpoints can find the user.</summary>
    private async Task SeedTestUserAsync()
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await userManager.FindByIdAsync(TestUserId.ToString());
        if (existing != null) return;

        var user = new ApplicationUser
        {
            Id = TestUserId,
            UserName = $"testmfa-{TestUserId:N}",
            Email = "testmfa@test.vn",
            EmailConfirmed = true,
            FullName = "MFA Test User",
            TenantId = TestTenantId,
            IsActive = true,
        };
        var result = await userManager.CreateAsync(user, "TestPass@12345!");
        result.Succeeded.Should().BeTrue(
            $"Failed to seed user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    // ── Begin enrolment ──────────────────────────────────────────────────────

    [Fact]
    public async Task BeginEnrol_AuthenticatedUser_Returns200WithQrUriAndSecret()
    {
        await SeedTestUserAsync();
        var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        var response = await client.GetAsync("/api/v1/account/mfa/enrol/totp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        var qrUri = data.GetProperty("qrCodeUri").GetString();
        qrUri.Should().StartWith("otpauth://totp/");
        qrUri.Should().Contain("secret=");

        var secret = data.GetProperty("secretBase32").GetString();
        secret.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BeginEnrol_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/account/mfa/enrol/totp");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BeginEnrol_ReturnsValidOtpauthUri()
    {
        await SeedTestUserAsync();
        var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        var response = await client.GetAsync("/api/v1/account/mfa/enrol/totp");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var qrUri = body.GetProperty("data").GetProperty("qrCodeUri").GetString()!;

        var parsed = new Uri(qrUri);
        parsed.Scheme.Should().Be("otpauth");
        parsed.Host.Should().Be("totp");
        qrUri.Should().Contain("secret=");
    }

    // ── Complete enrolment ───────────────────────────────────────────────────

    [Fact]
    public async Task CompleteEnrol_ValidTotpCode_Returns200WithRecoveryCodes()
    {
        await SeedTestUserAsync();
        var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Step 1: begin enrolment
        var beginResp = await client.GetAsync("/api/v1/account/mfa/enrol/totp");
        beginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var beginBody = await beginResp.Content.ReadFromJsonAsync<JsonElement>();
        var qrUri = beginBody.GetProperty("data").GetProperty("qrCodeUri").GetString()!;

        // Extract secret from otpauth URI
        var queryPairs = new Uri(qrUri).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
        var secret = queryPairs["secret"];
        secret.Should().NotBeNullOrEmpty();

        // Step 2: generate current TOTP code
        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes);
        var currentCode = totp.ComputeTotp();

        // Step 3: complete enrolment
        var completeResp = await client.PostAsJsonAsync(
            "/api/v1/account/mfa/enrol/totp",
            new { TotpCode = currentCode });

        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeBody = await completeResp.Content.ReadFromJsonAsync<JsonElement>();
        var recoveryCodes = completeBody.GetProperty("data").GetProperty("recoveryCodes");
        recoveryCodes.GetArrayLength().Should().Be(16, "should generate 16 recovery codes (phase-03 spec)");
    }

    [Fact]
    public async Task CompleteEnrol_InvalidCode_Returns400()
    {
        await SeedTestUserAsync();
        var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Begin first to initialise pending secret cache
        await client.GetAsync("/api/v1/account/mfa/enrol/totp");

        var completeResp = await client.PostAsJsonAsync(
            "/api/v1/account/mfa/enrol/totp",
            new { TotpCode = "000000" }); // always invalid

        completeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CompleteEnrol_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/mfa/enrol/totp",
            new { TotpCode = "123456" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Unenrol (requires [RequiresMfa]) ──────────────────────────────────────

    [Fact]
    public async Task UnenrolTotp_WithoutMfaSession_Returns403()
    {
        await SeedTestUserAsync();
        // Authenticated client has no mfa_at claim → [RequiresMfa] should deny
        var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        var response = await client.DeleteAsync("/api/v1/account/mfa/totp");

        // [RequiresMfa] returns 403 Forbidden when mfa_at claim is absent/stale
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnenrolTotp_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.DeleteAsync("/api/v1/account/mfa/totp");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Recovery code regeneration (requires [RequiresMfa]) ──────────────────

    [Fact]
    public async Task RegenerateRecoveryCodes_WithoutMfaSession_Returns403()
    {
        await SeedTestUserAsync();
        var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        var response = await client.PostAsync(
            "/api/v1/account/mfa/recovery-codes/regenerate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/account/mfa/recovery-codes/regenerate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── End-to-end: enrolment must enable subsequent MFA validation (ADR-007 regression guard) ──

    /// <summary>
    /// Regression test for the dual-storage contract introduced in ADR-007.
    ///
    /// Pre-fix bug (commit 59cce7d2): CompleteTotpEnrolAsync wrote pending secret to
    /// ApplicationUser.TotpSecret only. ValidateTotpAsync (and SignInManager.TwoFactorAuthenticatorSignInAsync)
    /// read from AspNetUserTokens via UserManager.GetAuthenticatorKeyAsync — different storage.
    /// Result: enrolled users could not validate TOTP at login. QĐ742 1.6.b non-functional.
    ///
    /// Post-fix: CompleteTotpEnrolAsync dual-writes (AspNetUserTokens primary + TotpSecret backup)
    /// and sets TwoFactorEnabled=true. This test asserts both storage backends agree AND
    /// IMfaService.ValidateTotpAsync returns true for a freshly-enrolled user.
    ///
    /// If this test fails, the dual-write contract has regressed — re-read ADR-007 before patching.
    /// </summary>
    [Fact]
    public async Task EnrolThenValidate_RealFlow_BothStorageBackendsAgree()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Seed a fresh user (separate from class-level TestUserId to avoid prior-state contamination)
        using (var seedScope = _app.Services.CreateScope())
        {
            var um = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = userId,
                UserName = $"mfa-e2e-{userId:N}",
                Email = $"mfa-e2e-{userId:N}@test.vn",
                EmailConfirmed = true,
                FullName = "MFA E2E User",
                TenantId = tenantId,
                IsActive = true,
            };
            var seedResult = await um.CreateAsync(user, "TestPass@12345!");
            seedResult.Succeeded.Should().BeTrue(
                $"seed failed: {string.Join(", ", seedResult.Errors.Select(e => e.Description))}");
        }

        var client = _app.CreateAuthenticatedClient(userId, tenantId);

        // 1. Begin enrolment — extracts pending secret from QR URI
        var beginResp = await client.GetAsync("/api/v1/account/mfa/enrol/totp");
        beginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var beginBody = await beginResp.Content.ReadFromJsonAsync<JsonElement>();
        var qrUri = beginBody.GetProperty("data").GetProperty("qrCodeUri").GetString()!;
        var query = new Uri(qrUri).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
        var pendingSecret = query["secret"];

        // 2. Compute current TOTP & complete enrolment
        var totp = new Totp(Base32Encoding.ToBytes(pendingSecret));
        var currentCode = totp.ComputeTotp();
        var completeResp = await client.PostAsJsonAsync(
            "/api/v1/account/mfa/enrol/totp", new { TotpCode = currentCode });
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Storage assertions — both backends must agree (ADR-007 dual-write contract)
        using var verifyScope = _app.Services.CreateScope();
        var verifyUm = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var enrolledUser = await verifyUm.FindByIdAsync(userId.ToString());
        enrolledUser.Should().NotBeNull();

        // Identity primary store — AspNetUserTokens via GetAuthenticatorKeyAsync
        var identityKey = await verifyUm.GetAuthenticatorKeyAsync(enrolledUser!);
        identityKey.Should().Be(pendingSecret,
            "AspNetUserTokens AuthenticatorKey must match the pending secret (ADR-007 primary store)");

        // Backup column — encrypted ApplicationUser.TotpSecret
        enrolledUser!.TotpSecret.Should().Be(pendingSecret,
            "ApplicationUser.TotpSecret must match the pending secret (ADR-007 encrypted backup)");
        enrolledUser.TotpEnrolledAt.Should().NotBeNull("TotpEnrolledAt must be stamped on success");

        // TwoFactorEnabled flag — required for SignInManager.RequiresTwoFactor branch
        var twoFactorEnabled = await verifyUm.GetTwoFactorEnabledAsync(enrolledUser);
        twoFactorEnabled.Should().BeTrue("TwoFactorEnabled must be set so SignInManager triggers MFA challenge");

        // 4. Validation path — IMfaService.ValidateTotpAsync (used by /connect/mfa-challenge) must succeed
        var mfaService = verifyScope.ServiceProvider.GetRequiredService<IMfaService>();
        var freshCode = totp.ComputeTotp();
        var validateResult = await mfaService.ValidateTotpAsync(userId, freshCode);
        validateResult.Should().BeTrue(
            "IMfaService.ValidateTotpAsync must succeed for a freshly-enrolled user " +
            "— this is the regression that ADR-007 dual-write fixes.");
    }
}
