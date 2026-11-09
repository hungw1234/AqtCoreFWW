using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Integration.Tests.Signing.Fakes;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// D6 acceptance gate — provider-agnostic extensibility (QĐ 742 §5.1, Amendment D6).
///
/// Mandate: adding a new VN CA provider MUST require zero changes to core
/// (resolver, handlers, controllers). A third-party provider is registered via keyed +
/// unkeyed DI and MUST be:
///   - Auto-discovered by GET /available-providers (proves IEnumerable&lt;IDigitalSignatureProvider&gt; enumeration)
///   - Selectable as a tenant's provider via PUT /signature-config + GET round-trip
///   - Callable end-to-end via POST /sign (proves resolver.Resolve + handler dispatch work without core change)
///
/// The fake provider is registered via WithWebHostBuilder (additive over real InfrastructureRegistration),
/// so the two production providers (remote-viettel-mysign, usb-vgca) remain registered and the test
/// confirms the new provider ADDS to the pool — zero core files are modified.
///
/// Behavioral unit tests (direct resolver construction, capabilities) remain in the unit suite
/// at AqtCoreFW.Identity.Infrastructure.Tests.Signing.ProviderExtensibilityTests.
/// This test class owns the HTTP / WebAppFixture boundary proof.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ProviderExtensibilityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid UserId = Guid.NewGuid();

    public ProviderExtensibilityTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Host wired with the fake third-party provider ────────────────────────────

    /// <summary>
    /// Creates a WebApplicationFactory with FakeThirdPartyVnCaProvider injected additively.
    /// Mirrors InfrastructureRegistration pattern: both keyed (for Resolve) + unkeyed (for enumeration).
    /// The factory is intentionally NOT cached — each test that needs it creates its own client
    /// so WithWebHostBuilder overrides compose cleanly and don't leak across tests.
    /// </summary>
    private HttpClient CreateFakeProviderClient(Guid tenantId, bool includeMfa = true)
    {
        var factory = _app.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var fake = new FakeThirdPartyVnCaProvider();
                // Keyed: used by ISignatureProviderResolver.Resolve(providerKey)
                services.AddKeyedSingleton<IDigitalSignatureProvider>("fake-thirdparty-ca", fake);
                // Unkeyed: used by ISignatureProviderResolver.GetAvailableProviders()
                // via IEnumerable<IDigitalSignatureProvider> injection
                services.AddSingleton<IDigitalSignatureProvider>(fake);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "SystemAdmin");
        if (includeMfa)
            client.DefaultRequestHeaders.Add(TestAuthHandler.MfaAtHeader,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }

    /// <summary>
    /// Seeds a TenantSignatureConfig row directly via EF (no HTTP dependency in arrange).
    /// Uses the shared WebAppFixture's real SQL Server — same DB the HTTP layer reads.
    /// </summary>
    private async Task SeedSignatureConfigAsync(Guid tenantId, string providerKey)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var config = TenantSignatureConfig.Create(
            tenantId: tenantId,
            providerKey: providerKey,
            apiCredentialsRef: null,
            allowedCaRootThumbprints: string.Empty,
            ocspMode: OcspMode.Soft,
            isEnabled: true,
            signFlowMode: SignatureFlowMode.Async,
            createdBy: UserId);

        db.Set<TenantSignatureConfig>().Add(config);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the signing user with a SigningNationalId. Remote signing is fail-closed when the signer
    /// has no national id (anti-impersonation), so /sign requires the user row to exist with it set.
    /// The fake provider returns no certificate, so the post-sign binding check is skipped.
    /// </summary>
    private async Task SeedSignerUserAsync(Guid tenantId, string signingNationalId)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();

        if (await userManager.FindByIdAsync(UserId.ToString()) is not null) return;

        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = $"signer-{UserId:N}",
            Email = $"signer-{UserId:N}@example.com",
            TenantId = tenantId,
            SigningNationalId = signingNationalId,
        };
        var result = await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue("signer user seed should succeed");
    }

    // ── GET /available-providers: fake provider auto-discovered ──────────────────

    [Fact]
    public async Task GetAvailableProviders_FakeRegistered_IncludesFakeInList()
    {
        // D6 proof (enumeration path):
        // FakeThirdPartyVnCaProvider registered via additive DI — NO change to core resolver/handlers.
        // GET /available-providers must return it alongside the two production providers,
        // proving IEnumerable<IDigitalSignatureProvider> auto-discovers all registered providers.

        var tenantId = Guid.NewGuid();
        var client = CreateFakeProviderClient(tenantId);

        var response = await client
            .GetAsync("/api/v1/admin/security/signature-config/available-providers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        data.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);

        var keys = Enumerable.Range(0, data.GetArrayLength())
            .Select(i => data[i].GetProperty("providerKey").GetString())
            .ToList();

        // Known production providers are still present (additive — not replaced)
        keys.Should().Contain("remote-viettel-mysign",
            "production providers must still be enumerated after additive fake registration");
        keys.Should().Contain("usb-vgca",
            "production providers must still be enumerated after additive fake registration");

        // Fake third-party discovered with zero core changes — D6 mandate
        keys.Should().Contain("fake-thirdparty-ca",
            "D6: third-party provider registered via keyed DI must be auto-discovered " +
            "via IEnumerable<IDigitalSignatureProvider> without modifying resolver or handlers");
    }

    // ── Config round-trip: fake provider key accepted + readable back ────────────

    [Fact]
    public async Task Put_FakeProviderKey_Accepted_GetReadsBackSameKey()
    {
        // D6 proof (config storage path):
        // Validator accepts any non-empty ProviderKey (no whitelist) — new providers need
        // zero changes to the validator or handler to be stored as a tenant's config.

        var tenantId = Guid.NewGuid();
        var client = CreateFakeProviderClient(tenantId);

        var putBody = new
        {
            ProviderKey = "fake-thirdparty-ca",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };

        var putResponse = await client
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", putBody);

        putResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "D6: UpdateTenantSignatureConfigCommand validator accepts any non-empty ProviderKey — " +
            "no whitelist change needed per new provider");

        // Verify stored value reads back correctly
        var getResponse = await client
            .GetAsync($"/api/v1/admin/security/signature-config/{tenantId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await getResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        data.GetProperty("providerKey").GetString().Should().Be("fake-thirdparty-ca",
            "D6: config round-trip must preserve the third-party provider key verbatim");
    }

    // ── POST /sign: fake provider dispatched end-to-end ──────────────────────────

    [Fact]
    public async Task Sign_FakeProviderKey_Configured_Returns200WithFakeSignature()
    {
        // D6 proof (signing dispatch path — the critical zero-touch gate):
        // 1. Seed tenant config pointing at fake-thirdparty-ca (EF direct write, no HTTP)
        // 2. POST /sign — handler calls resolver.Resolve("fake-thirdparty-ca")
        //    → resolver finds it via IServiceProvider.GetKeyedService (registered via AddKeyedSingleton)
        //    → FakeThirdPartyVnCaProvider.SignAsync returns [0xFA, 0xCE]
        //    → handler returns success with that signature
        // Core files (resolver, handler, controller) are NOT modified — D6 proven.

        var tenantId = Guid.NewGuid();
        await SeedSignatureConfigAsync(tenantId, "fake-thirdparty-ca");
        // Remote signing is fail-closed without a signer national id (anti-impersonation, C1).
        await SeedSignerUserAsync(tenantId, signingNationalId: "079200001234");

        var client = CreateFakeProviderClient(tenantId);
        var body = new
        {
            Payload = new byte[] { 0x01, 0x02, 0x03 },
            Reason = (string?)null,
            ExtraParameters = (object?)null
        };

        var response = await client
            .PostAsJsonAsync("/api/v1/digital-signature/sign", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "D6: SignPayloadCommandHandler dispatches to fake-thirdparty-ca via resolver " +
            "with zero core changes — no KeyNotFoundException, no 500");

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");

        data.GetProperty("isSuccess").GetBoolean().Should().BeTrue(
            "FakeThirdPartyVnCaProvider.SignAsync always returns IsSuccess=true");

        // Signature bytes [0xFA, 0xCE] — stable contract from FakeThirdPartyVnCaProvider
        var signatureBase64 = data.GetProperty("signature").GetString();
        signatureBase64.Should().NotBeNullOrEmpty("signature bytes must be returned by fake provider");
        var signatureBytes = Convert.FromBase64String(signatureBase64!);
        signatureBytes.Should().BeEquivalentTo(new byte[] { 0xFA, 0xCE },
            "D6: signature from FakeThirdPartyVnCaProvider is [0xFA, 0xCE] — " +
            "proving the fake adapter's SignAsync was invoked end-to-end");
    }
}
