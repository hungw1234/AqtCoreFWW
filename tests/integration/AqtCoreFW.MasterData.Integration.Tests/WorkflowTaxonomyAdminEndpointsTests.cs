using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.MasterData.Integration.Tests;

/// <summary>
/// Integration tests for Phase-00 WorkflowTaxonomyAdminController (10 endpoints).
///
/// Covers plan C3 BLOCKING items:
///  â€¢ 401 unauthenticated, 403 no-permission, 200/201 happy-path
///  â€¢ 409 concurrency (RowVersion mismatch), 409 duplicate code, 409 already active
///  â€¢ 400 confirmCode mismatch
///  â€¢ 409 "Draft" vs "DRAFT" collision (SQL Server CI_AS default collation)
///  â€¢ DELETE response includes referencedByDefinitionCount placeholder
///  â€¢ Restore flow end-to-end
///
/// Permission model: PermissionAuthorizationHandler â†’ IEffectivePermissionService.
/// Testing env has no identity data seeded, so tests stub IEffectivePermissionService
/// via ConfigureTestServices (same pattern as Workflow integration tests â€” see
/// WorkflowInstancesListEndpointTests.CreatePermittedClient).
///
/// Cache hit/miss not asserted here (Redis disabled in tests) â€” unit tests cover that path.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowTaxonomyAdminEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.Parse("A0000000-0000-0000-0000-0000000000C3");
    private static readonly Guid AdminId  = Guid.Parse("B0000000-0000-0000-0000-0000000000C3");

    private const string StatesRoute  = "/api/v1/admin/master-data/workflow-taxonomy/states";
    private const string ActionsRoute = "/api/v1/admin/master-data/workflow-taxonomy/actions";

    public WorkflowTaxonomyAdminEndpointsTests(WebAppFixture app) => _app = app;

    // â”€â”€ Auth enforcement â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task ListStates_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();
        var response = await client.GetAsync(StatesRoute);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListActions_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();
        var response = await client.GetAsync(ActionsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateState_NoPlatformPermission_Returns403()
    {
        // Authed user with NO permissions in effective summary â†’ PermissionAuthorizationHandler denies.
        var client = CreateClientWithPermissions(AdminId, TenantId, permissions: []);
        var response = await client.PostAsJsonAsync(StatesRoute, new
        {
            code = $"T_{Guid.NewGuid():N}",
            nameVi = "Thá»­", nameEn = "Test",
            category = "Intermediate", sortOrder = 0
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // â”€â”€ Happy path: reads (VIEW permission) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task ListStates_WithViewPermission_Returns200_WithApiEnvelope()
    {
        var client = CreateViewClient();
        var response = await client.GetAsync(StatesRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.TryGetProperty("data", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ListActions_WithViewPermission_Returns200_WithDataArray()
    {
        // Testing env strips hosted services (including MasterDataSeeder), so row count is not
        // seeder-dependent here. Seeder behavior is covered by MasterDataSeederTests unit suite.
        var client = CreateViewClient();
        var response = await client.GetAsync(ActionsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Array);
    }

    // â”€â”€ Happy path: writes (MANAGE permission) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task CreateState_WithManagePermission_Returns201_WithIdInBody()
    {
        var client = CreateManageClient();
        var code = UniqueCode("Create");

        var response = await client.PostAsJsonAsync(StatesRoute, new
        {
            code,
            nameVi = "Táº¡o má»›i", nameEn = "Created",
            category = "Intermediate", sortOrder = 100
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateState_CaseInsensitiveDuplicate_ReturnsConflict_DueToCiCollation()
    {
        // SQL Server CI_AS default collation enforces case-insensitive uniqueness on Code.
        // Seeder is stripped in Testing env, so we self-seed the initial code via API, then
        // assert a second create with a differently-cased variant hits the unique-index conflict.
        var client = CreateManageClient();
        var baseCode = $"CiTest_{Guid.NewGuid():N}"[..20];

        var first = await client.PostAsJsonAsync(StatesRoute, new
        {
            code = baseCode,
            nameVi = "NhÃ¡p", nameEn = "Draft",
            category = "Initial", sortOrder = 1
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync(StatesRoute, new
        {
            code = baseCode.ToUpperInvariant(), // differs only in case
            nameVi = "NhÃ¡p upper", nameEn = "Draft upper",
            category = "Initial", sortOrder = 2
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].GetProperty("code").GetString().Should().Be("GOV_DUPLICATE_CODE");
    }

    [Fact]
    public async Task UpdateState_StaleRowVersion_Returns409_ConcurrencyConflict()
    {
        var client = CreateManageClient();
        var code = UniqueCode("Conc");
        var id = await CreateStateAsync(client, code);

        var (_, rowVersion) = await GetStateAsync(client, id);
        var staleRowVersion = new byte[rowVersion.Length]; // zeros never match live value

        var response = await client.PutAsJsonAsync($"{StatesRoute}/{id}", new
        {
            nameVi = "Updated", nameEn = "Updated",
            category = "Intermediate", sortOrder = 50,
            rowVersion = Convert.ToBase64String(staleRowVersion)
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("GOV_CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task DeleteState_ConfirmCodeMismatch_Returns400()
    {
        var client = CreateManageClient();
        var code = UniqueCode("Del");
        var id = await CreateStateAsync(client, code);

        // confirmCode case-SENSITIVE per plan R7 â†’ wrong case = 400
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{StatesRoute}/{id}")
        {
            Content = JsonContent.Create(new { confirmCode = code.ToLowerInvariant(), reason = "Mismatch test reason 10+" })
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("GOV_CONFIRM_CODE_MISMATCH");
    }

    [Fact]
    public async Task DeleteState_HappyPath_Returns200_WithReferencedByCountPlaceholder()
    {
        var client = CreateManageClient();
        var code = UniqueCode("DelOk");
        var id = await CreateStateAsync(client, code);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{StatesRoute}/{id}")
        {
            Content = JsonContent.Create(new { confirmCode = code, reason = "Deprecated via integration test." })
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("referencedByDefinitionCount").GetInt32()
            .Should().Be(0, "phase-00 placeholder until Casesâ†’Workflow wires real count");
    }

    [Fact]
    public async Task RestoreState_AfterDelete_ReactivatesRow()
    {
        var client = CreateManageClient();
        var code = UniqueCode("Rest");
        var id = await CreateStateAsync(client, code);

        var deleteResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{StatesRoute}/{id}")
        {
            Content = JsonContent.Create(new { confirmCode = code, reason = "Restore flow integration test." })
        });
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResp = await client.PostAsync($"{StatesRoute}/{id}/restore", content: null);
        restoreResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await client.GetFromJsonAsync<JsonElement>($"{StatesRoute}/{id}");
        detail.GetProperty("data").GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RestoreState_AlreadyActive_Returns409_GovAlreadyActive()
    {
        var client = CreateManageClient();
        var code = UniqueCode("AlrAct");
        var id = await CreateStateAsync(client, code);

        // Restore on non-deprecated row â†’ 409 GOV_ALREADY_ACTIVE per C2 M1 guard
        var response = await client.PostAsync($"{StatesRoute}/{id}/restore", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("GOV_ALREADY_ACTIVE");
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private HttpClient CreateViewClient() =>
        CreateClientWithPermissions(AdminId, TenantId,
            permissions: [PermCodes.Master.WorkflowTaxonomy.View]);

    private HttpClient CreateManageClient() =>
        CreateClientWithPermissions(AdminId, TenantId,
            permissions: [
                PermCodes.Master.WorkflowTaxonomy.View,
                PermCodes.Platform.WorkflowTaxonomy.Manage
            ]);

    /// <summary>
    /// Creates an authenticated client whose IEffectivePermissionService grants only the
    /// specified permissions â€” other users get empty set (â†’ 403). Other tests in the same
    /// fixture are unaffected because WithWebHostBuilder clones the server per call.
    /// </summary>
    private HttpClient CreateClientWithPermissions(Guid userId, Guid tenantId, string[] permissions)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string>(permissions)
            });
        permService
            .GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });

        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        // WorkflowTaxonomyAdminController carries [RequiresMfa] — supply a fresh mfa_at claim
        // so the MfaRequirement passes and assertions exercise the permission layer, not MFA.
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaAtHeader,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }

    private static string UniqueCode(string prefix)
    {
        var raw = $"{prefix}_{Guid.NewGuid():N}";
        return raw.Length > 63 ? raw[..63] : raw;
    }

    private static async Task<Guid> CreateStateAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync(StatesRoute, new
        {
            code,
            nameVi = $"VN_{code}", nameEn = $"EN_{code}",
            category = "Intermediate", sortOrder = 0
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<(JsonElement Detail, byte[] RowVersion)> GetStateAsync(HttpClient client, Guid id)
    {
        var body = await client.GetFromJsonAsync<JsonElement>($"{StatesRoute}/{id}");
        var data = body.GetProperty("data");
        var rv = Convert.FromBase64String(data.GetProperty("rowVersion").GetString()!);
        return (data, rv);
    }
}
