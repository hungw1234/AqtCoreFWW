using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.AssetManagement.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.AssetManagement.Integration.Tests;

[Collection(SqlServerCollection.CollectionName)]
public sealed class AssetManagementEndpointsTests : IClassFixture<WebAppFixture>
{
    private const string Route = "/api/v1/asset-management";
    private static readonly Guid TenantA = Guid.Parse("10000000-0000-0000-0000-0000000000A1");
    private static readonly Guid UserId = Guid.Parse("AD000000-0000-0000-0000-0000000000E1");

    private static string UniqueTitle(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private readonly WebAppFixture _app;

    public AssetManagementEndpointsTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_ValidPayload_Returns200AndPersistsAssetWithTenantAndActorFromClaims()
    {
        await SetCreateFeatureFlagAsync(TenantA, enabled: true);
        var client = _app.CreateAuthenticatedClient(UserId, TenantA);
        var title = UniqueTitle("AssetCreate");

        var response = await client.PostAsJsonAsync(Route, new
        {
            title,
            description = "Integration test asset",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        var assetId = body.GetProperty("data").GetInt64();
        assetId.Should().BeGreaterThan(0);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetManagementDbContext>();
        var saved = await db.AssetManagements
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(asset => asset.Id == assetId);

        saved.TenantId.Should().Be(TenantA, "tenant must come from claims");
        saved.CreatedBy.Should().Be(UserId, "actor must come from claims");
        saved.Title.Should().Be(title);
        saved.Description.Should().Be("Integration test asset");
    }

    [Fact]
    public async Task CrudLifecycle_Authenticated_PersistsAndReturnsAsset()
    {
        await SetCreateFeatureFlagAsync(TenantA, enabled: true);
        var client = _app.CreateAuthenticatedClient(UserId, TenantA);
        var title = UniqueTitle("AssetCrud");

        var createResponse = await client.PostAsJsonAsync(Route, new
        {
            title,
            description = "Integration test asset",
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        createBody.GetProperty("success").GetBoolean().Should().BeTrue();
        var assetId = createBody.GetProperty("data").GetInt64();
        assetId.Should().BeGreaterThan(0);

        var getResponse = await client.GetAsync($"{Route}/{assetId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        getBody.GetProperty("data").GetProperty("title").GetString().Should().Be(title);

        var updatedTitle = $"{title}-updated";
        var updateResponse = await client.PutAsJsonAsync($"{Route}/{assetId}", new
        {
            title = updatedTitle,
            description = "Updated by integration test",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await client.GetAsync(Route);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        listBody.GetProperty("success").GetBoolean().Should().BeTrue();
        listBody.GetProperty("data").EnumerateArray()
            .Should().ContainSingle(asset => asset.GetProperty("id").GetInt64() == assetId &&
                                              asset.GetProperty("title").GetString() == updatedTitle);

        var deleteResponse = await client.DeleteAsync($"{Route}/{assetId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deletedGetResponse = await client.GetAsync($"{Route}/{assetId}");
        deletedGetResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_FeatureFlagDisabled_Returns403()
    {
        await SetCreateFeatureFlagAsync(TenantA, enabled: false);
        var client = _app.CreateAuthenticatedClient(UserId, TenantA);

        var response = await client.PostAsJsonAsync(Route, new
        {
            title = UniqueTitle("AssetFlagDisabled"),
            description = "Creation is disabled for this tenant",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_AnotherTenantCannotReadAsset()
    {
        await SetCreateFeatureFlagAsync(TenantA, enabled: true);
        var ownerClient = _app.CreateAuthenticatedClient(UserId, TenantA);
        var createResponse = await ownerClient.PostAsJsonAsync(Route, new
        {
            title = UniqueTitle("AssetTenantIsolation"),
            description = "Tenant-scoped asset",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var assetId = createBody.GetProperty("data").GetInt64();

        var otherTenantClient = _app.CreateAuthenticatedClient(
            Guid.NewGuid(), Guid.NewGuid());
        var getResponse = await otherTenantClient.GetAsync($"{Route}/{assetId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task SetCreateFeatureFlagAsync(Guid tenantId, bool enabled)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemParamsDbContext>();
        var tenantKey = tenantId.ToString();
        var flag = await db.SystemParameters
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(parameter =>
                parameter.Key == "feature:assetmanagement.create" &&
                parameter.TenantId == tenantKey);

        if (flag is null)
        {
            db.SystemParameters.Add(SystemParameter.Create(
                "feature:assetmanagement.create",
                enabled.ToString(),
                SystemParamDataType.Bool,
                "AssetManagement integration test feature flag",
                isEditable: true,
                tenantId: tenantKey));
        }
        else
        {
            flag.UpdateValue(enabled.ToString());
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider
            .GetRequiredService<FeatureFlagService>()
            .ReloadAllAsync();
    }
}
