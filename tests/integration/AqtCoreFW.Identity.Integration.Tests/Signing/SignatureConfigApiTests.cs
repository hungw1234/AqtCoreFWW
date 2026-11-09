using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// HTTP boundary tests for <c>SignatureConfigAdminController</c> (QĐ 742 §5.1 admin CRUD).
///
/// Covers:
///   - 401 anonymous
///   - 403 no MFA claim (RequiresMfa enforcement)
///   - 403 regular user without SECURITY.SIGNATURE_CONFIG.VIEW permission
///   - 404 GET for tenant with no config
///   - 200 GET after config upserted
///   - 204 PUT upsert (create + update)
///   - 400 / 422 invalid PUT body
///   - 200 GET available-providers
///
/// Uses WebAppFixture (real SQL Server) + TestAuthHandler (bypasses JWT).
/// SystemAdmin role satisfies all PermissionRequirement gates via TestSystemAdminPermissionBypassHandler.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SignatureConfigApiTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid SystemAdminUserId = Guid.NewGuid();
    private static readonly Guid RegularUserId = Guid.NewGuid();

    // Unique tenant ID per test class to avoid config row collision across parallel runs.
    private static readonly Guid TenantId = Guid.NewGuid();

    public SignatureConfigApiTests(WebAppFixture app)
    {
        _app = app;
    }

    private HttpClient SystemAdminClient() =>
        _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: true, "SystemAdmin");

    private HttpClient RegularUserClient() =>
        _app.CreateAuthenticatedClient(RegularUserId, TenantId, includeMfaClaim: true);

    private HttpClient MfalessAdminClient() =>
        _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: false, "SystemAdmin");

    private HttpClient AnonymousClient() => _app.CreateClient();

    // ── GET /api/v1/admin/security/signature-config/{tenantId} ───────────────────

    [Fact]
    public async Task Get_Anonymous_Returns401()
    {
        var response = await AnonymousClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{TenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_NoMfaClaim_Returns403()
    {
        var response = await MfalessAdminClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{TenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "SignatureConfigAdminController has class-level [RequiresMfa] — no MFA claim = 403");
    }

    [Fact]
    public async Task Get_RegularUserWithoutPermission_Returns403()
    {
        var response = await RegularUserClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "regular user has no SECURITY.SIGNATURE_CONFIG.VIEW permission");
    }

    [Fact]
    public async Task Get_SystemAdmin_NoConfig_Returns404()
    {
        // Tenant with no signature config configured yet.
        var unknownTenantId = Guid.NewGuid();

        var response = await SystemAdminClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{unknownTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_SystemAdmin_AfterUpsert_Returns200WithConfig()
    {
        // Arrange: upsert a config first
        var tenantId = Guid.NewGuid(); // isolated tenant ID
        var putBody = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };
        var put = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", putBody);
        put.StatusCode.Should().Be(HttpStatusCode.NoContent, "PUT upsert must succeed first");

        // Act: GET the config
        var response = await SystemAdminClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        // Response is wrapped in ApiResponse envelope: { success, data, errors }
        var data = json.GetProperty("data");
        data.GetProperty("providerKey").GetString().Should().Be("remote-viettel-mysign");
        data.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
    }

    // ── PUT /api/v1/admin/security/signature-config/{tenantId} ──────────────────

    [Fact]
    public async Task Put_Anonymous_Returns401()
    {
        var body = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };

        var response = await AnonymousClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Put_NoMfaClaim_Returns403()
    {
        var body = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };

        var response = await MfalessAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_RegularUserWithoutPermission_Returns403()
    {
        var body = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };

        var response = await RegularUserClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PUT requires SECURITY.SIGNATURE_CONFIG.MANAGE — regular user lacks this");
    }

    [Fact]
    public async Task Put_SystemAdmin_CreateNew_Returns204()
    {
        var tenantId = Guid.NewGuid(); // isolated to avoid collision
        var body = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };

        var response = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Put_SystemAdmin_UpdateExisting_Returns204()
    {
        // First create
        var tenantId = Guid.NewGuid();
        var createBody = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };
        await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", createBody);

        // Then update — change provider key and IsEnabled
        var updateBody = new
        {
            ProviderKey = "usb-vgca",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Hard,
            IsEnabled = false
        };
        var response = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", updateBody);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the update took effect
        var getResponse = await SystemAdminClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{tenantId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await getResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        data.GetProperty("providerKey").GetString().Should().Be("usb-vgca");
        data.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Put_EmptyProviderKey_Returns422()
    {
        var body = new
        {
            ProviderKey = "",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };

        var response = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{Guid.NewGuid()}", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "FluentValidation: ProviderKey must not be empty");
    }

    // ── Optimistic concurrency (C4) ─────────────────────────────────────────────

    [Fact]
    public async Task Put_StaleRowVersion_Returns409()
    {
        // C4: Stale-tab race — supplying an outdated RowVersion must produce 409 Conflict.
        // Flow:
        //   1. PUT (no RowVersion) → creates config; DB assigns RowVersion v1.
        //   2. GET  → read current base64 RowVersion.
        //   3. PUT  → update without RowVersion to advance DB to RowVersion v2.
        //   4. PUT  → supply the *old* v1 RowVersion — concurrency guard fires → 409.

        var tenantId = Guid.NewGuid();

        // Step 1: create config (no RowVersion on first creation)
        var createBody = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true
        };
        var create = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", createBody);
        create.StatusCode.Should().Be(HttpStatusCode.NoContent, "initial create must succeed");

        // Step 2: GET to read the current (v1) RowVersion
        var get = await SystemAdminClient()
            .GetAsync($"/api/v1/admin/security/signature-config/{tenantId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var v1RowVersion = json.GetProperty("data").GetProperty("rowVersion").GetString();
        v1RowVersion.Should().NotBeNullOrEmpty("GET response must include base64 RowVersion");

        // Step 3: Update without RowVersion — advances DB RowVersion to v2
        var advanceBody = new
        {
            ProviderKey = "usb-vgca",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Hard,
            IsEnabled = true
            // RowVersion intentionally omitted — null = skip concurrency check (backward-compat)
        };
        var advance = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", advanceBody);
        advance.StatusCode.Should().Be(HttpStatusCode.NoContent, "advancing PUT must succeed");

        // Step 4: PUT with the stale v1 RowVersion — EF WHERE RowVersion = v1 finds no row → 409
        var staleBody = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = false,
            RowVersion = v1RowVersion   // v1 is now stale — DB holds v2
        };
        var stale = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", staleBody);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "supplying a stale RowVersion must trigger DbUpdateConcurrencyException → GlobalExceptionHandlerMiddleware → 409");
    }

    [Fact]
    public async Task Put_InvalidBase64RowVersion_Returns400()
    {
        // C4: Malformed base64 RowVersion must be rejected with 400 before reaching the DB.
        var tenantId = Guid.NewGuid();
        var body = new
        {
            ProviderKey = "remote-viettel-mysign",
            ApiCredentialsRef = (string?)null,
            AllowedCaRootThumbprints = (string?)null,
            OcspMode = OcspMode.Soft,
            IsEnabled = true,
            RowVersion = "!!!not-valid-base64!!!"
        };

        var response = await SystemAdminClient()
            .PutAsJsonAsync($"/api/v1/admin/security/signature-config/{tenantId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "controller must decode base64 RowVersion and return 400 on FormatException");
    }

    // ── GET /api/v1/admin/security/signature-config/available-providers ──────────

    [Fact]
    public async Task GetAvailableProviders_Anonymous_Returns401()
    {
        var response = await AnonymousClient()
            .GetAsync("/api/v1/admin/security/signature-config/available-providers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAvailableProviders_NoMfaClaim_Returns403()
    {
        var response = await MfalessAdminClient()
            .GetAsync("/api/v1/admin/security/signature-config/available-providers");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAvailableProviders_SystemAdmin_Returns200WithList()
    {
        var response = await SystemAdminClient()
            .GetAsync("/api/v1/admin/security/signature-config/available-providers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        data.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        // Both providers are registered — at least 2 in response
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(2,
            "both usb-vgca and remote-viettel-mysign providers are registered in InfrastructureRegistration");
    }
}
