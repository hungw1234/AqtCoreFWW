using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// HTTP boundary tests for <c>DigitalSignatureController</c> (QĐ 742 §5.1 user-facing sign/verify).
///
/// Covers:
///   - 401 anonymous (sign + verify)
///   - 403 no MFA claim (RequiresMfa enforcement on both endpoints)
///   - 404 sign/verify when tenant has no signature config
///   - 409 sign when config is disabled
///   - 409 sign when provider uses client-side signing (USB token)
///   - 400 / 422 empty payload / signature
///   - 200 verify happy path (note: sign happy path requires live Viettel API — tested via WireMock in ViettelMysignSyncTests)
///
/// Notes:
///   - Sign 200 happy path is NOT tested here — it would call ViettelMysignProvider which requires
///     WireMock or live Viettel Mysign. That is covered by ViettelMysignSyncTests (W4 integration).
///   - Verify uses real BouncyCastle but non-CMS bytes → returns 200 IsValid=false (not 5xx).
///   - OCSP is Soft by default — Viettel OCSP unreachable in test env does not fail verify.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DigitalSignatureApiTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Unique user/tenant IDs per test class
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AnonymousTenantId = Guid.NewGuid();

    // Shared payload for all sign/verify requests
    private static readonly byte[] TestPayload = [0x01, 0x02, 0x03, 0x04];
    private static readonly byte[] NonCmsSignatureBytes = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02];

    public DigitalSignatureApiTests(WebAppFixture app)
    {
        _app = app;
    }

    private HttpClient AuthenticatedClient(bool includeMfa = true) =>
        _app.CreateAuthenticatedClient(UserId, AnonymousTenantId, includeMfaClaim: includeMfa);

    private HttpClient AnonymousClient() => _app.CreateClient();

    /// <summary>Seeds a TenantSignatureConfig row for the given tenant. Returns that tenant's HTTP client.</summary>
    private async Task<(HttpClient client, Guid tenantId)> SeedConfigAsync(
        string providerKey = "remote-viettel-mysign",
        bool isEnabled = true,
        OcspMode ocspMode = OcspMode.Soft)
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var config = TenantSignatureConfig.Create(
            tenantId: tenantId,
            providerKey: providerKey,
            apiCredentialsRef: null,
            allowedCaRootThumbprints: string.Empty,
            ocspMode: ocspMode,
            isEnabled: isEnabled,
            signFlowMode: SignatureFlowMode.Async,
            createdBy: actorId);

        db.Set<TenantSignatureConfig>().Add(config);
        await db.SaveChangesAsync();

        var client = _app.CreateAuthenticatedClient(UserId, tenantId, includeMfaClaim: true);
        return (client, tenantId);
    }

    // ── POST /api/v1/digital-signature/sign ──────────────────────────────────────

    [Fact]
    public async Task Sign_Anonymous_Returns401()
    {
        var body = new { Payload = TestPayload, Reason = (string?)null, ExtraParameters = (object?)null };

        var response = await AnonymousClient()
            .PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sign_NoMfaClaim_Returns403()
    {
        // [RequiresMfa] is class-level on DigitalSignatureController
        var noMfaClient = _app.CreateAuthenticatedClient(UserId, AnonymousTenantId, includeMfaClaim: false);
        var body = new { Payload = TestPayload, Reason = (string?)null, ExtraParameters = (object?)null };

        var response = await noMfaClient
            .PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "DigitalSignatureController has class-level [RequiresMfa] — signing requires a valid MFA session");
    }

    [Fact]
    public async Task Sign_NoConfig_Returns404()
    {
        // Tenant with no TenantSignatureConfig row
        var noConfigTenantId = Guid.NewGuid();
        var client = _app.CreateAuthenticatedClient(UserId, noConfigTenantId, includeMfaClaim: true);
        var body = new { Payload = TestPayload, Reason = (string?)null, ExtraParameters = (object?)null };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "GetByTenantIdAsync returns null → handler emits NotFoundError → 404");
    }

    [Fact]
    public async Task Sign_ConfigDisabled_Returns409()
    {
        // Tenant config exists but IsEnabled = false
        var (client, _) = await SeedConfigAsync(isEnabled: false);
        var body = new { Payload = TestPayload, Reason = (string?)null, ExtraParameters = (object?)null };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "handler returns ConflictError when signing is disabled for the tenant");
    }

    [Fact]
    public async Task Sign_ClientSideProviderKey_Returns409()
    {
        // USB token provider uses ClientSideSigning — server-side sign must be rejected
        var (client, _) = await SeedConfigAsync(providerKey: "usb-vgca", isEnabled: true);
        var body = new { Payload = TestPayload, Reason = (string?)null, ExtraParameters = (object?)null };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "handler emits ConflictError when provider lacks ServerSideSigning capability");
    }

    [Fact]
    public async Task Sign_EmptyPayload_Returns422()
    {
        var (client, _) = await SeedConfigAsync();
        var body = new { Payload = Array.Empty<byte>(), Reason = (string?)null, ExtraParameters = (object?)null };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "SignPayloadCommandValidator: Payload must not be empty");
    }

    // ── POST /api/v1/digital-signature/verify ────────────────────────────────────

    [Fact]
    public async Task Verify_Anonymous_Returns401()
    {
        var body = new { Payload = TestPayload, Signature = NonCmsSignatureBytes, OcspModeOverride = (OcspMode?)null };

        var response = await AnonymousClient()
            .PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Verify_NoMfaClaim_Returns403()
    {
        var noMfaClient = _app.CreateAuthenticatedClient(UserId, AnonymousTenantId, includeMfaClaim: false);
        var body = new { Payload = TestPayload, Signature = NonCmsSignatureBytes, OcspModeOverride = (OcspMode?)null };

        var response = await noMfaClient
            .PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Verify_NoConfig_Returns404()
    {
        var noConfigTenantId = Guid.NewGuid();
        var client = _app.CreateAuthenticatedClient(UserId, noConfigTenantId, includeMfaClaim: true);
        var body = new { Payload = TestPayload, Signature = NonCmsSignatureBytes, OcspModeOverride = (OcspMode?)null };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Verify_ConfigDisabled_Returns409()
    {
        var (client, _) = await SeedConfigAsync(isEnabled: false);
        var body = new { Payload = TestPayload, Signature = NonCmsSignatureBytes, OcspModeOverride = (OcspMode?)null };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Verify_NonCmsSignatureBytes_Returns200WithIsValidFalse()
    {
        // Real BouncyCastle CMS parse of random bytes fails gracefully with IsValid=false.
        // This proves the full stack (controller → handler → resolver → RemoteSigningProvider.VerifyAsync)
        // runs end-to-end without exception, returning a structured failure result.
        var (client, _) = await SeedConfigAsync(providerKey: "remote-viettel-mysign");
        var body = new
        {
            Payload = TestPayload,
            Signature = NonCmsSignatureBytes,
            OcspModeOverride = (OcspMode?)null
        };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "non-CMS bytes produce IsValid=false result (not an exception); stack returns 200 with failure detail");

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        data.GetProperty("isValid").GetBoolean().Should().BeFalse(
            "random bytes are not a valid CMS/PKCS#7 structure");
    }

    [Fact]
    public async Task Verify_EmptyPayload_Returns422()
    {
        var (client, _) = await SeedConfigAsync();
        var body = new
        {
            Payload = Array.Empty<byte>(),
            Signature = NonCmsSignatureBytes,
            OcspModeOverride = (OcspMode?)null
        };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "VerifySignatureCommandValidator: Payload must not be empty");
    }

    [Fact]
    public async Task Verify_EmptySignature_Returns422()
    {
        var (client, _) = await SeedConfigAsync();
        var body = new
        {
            Payload = TestPayload,
            Signature = Array.Empty<byte>(),
            OcspModeOverride = (OcspMode?)null
        };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "VerifySignatureCommandValidator: Signature bytes must not be empty");
    }

    [Fact]
    public async Task Verify_UsbVgcaProvider_NonCmsBytes_Returns200WithIsValidFalse()
    {
        // USB token provider also implements VerifyAsync — same graceful CMS parse failure path.
        var (client, _) = await SeedConfigAsync(providerKey: "usb-vgca");
        var body = new
        {
            Payload = TestPayload,
            Signature = NonCmsSignatureBytes,
            OcspModeOverride = (OcspMode?)null
        };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("data").GetProperty("isValid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Verify_OcspModeOverride_AcceptedByApi_Returns200()
    {
        // Verify that OcspModeOverride is correctly deserialized and accepted (not rejected as invalid).
        // The override doesn't affect the parse-failure outcome of non-CMS bytes.
        var (client, _) = await SeedConfigAsync(providerKey: "remote-viettel-mysign", ocspMode: OcspMode.Soft);
        var body = new
        {
            Payload = TestPayload,
            Signature = NonCmsSignatureBytes,
            OcspModeOverride = OcspMode.Hard  // caller overrides tenant's Soft config
        };

        var response = await client.PostAsJsonAsync("/api/v1/digital-signature/verify", body);

        // Non-CMS bytes still fail, but we get 200 (not 400/422) — override was accepted
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "OcspModeOverride=Hard is valid — the CMS parse failure (non-CMS bytes) produces IsValid=false, not a validation error");
    }
}
