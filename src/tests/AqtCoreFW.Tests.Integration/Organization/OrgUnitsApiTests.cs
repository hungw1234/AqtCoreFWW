using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Organization;

/// <summary>
/// Smoke tests for OrgUnitsController — /api/v1/admin/org/units
/// GET tree/members: [Authorize] (any authenticated user)
/// POST/PUT/DELETE: [Authorize(Roles = "Admin,SystemAdmin")]
/// tenantId is a required query parameter on all endpoints.
/// </summary>
[Collection("Integration")]
public class OrgUnitsApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/admin/org/units";
    private static readonly Guid TestTenantId = Guid.NewGuid();

    [Fact]
    public async Task GetOrgTree_AsSystemAdmin_Returns200()
    {
        var response = await Client.GetAsync(
            $"{BaseUrl}?tenantId={TestTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrgTree_AsViewer_Returns200()
    {
        // GET is open to any authenticated user (no perm policy), BUT controller still calls
        // ResolveTenantId() which reads the JWT tenant claim — ignores `?tenantId=` query param.
        // Must pass tenantId via CreateAuthenticatedClient so the claim is set.
        using var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"],
            tenantId: TestTenantId.ToString());

        var response = await client.GetAsync(
            $"{BaseUrl}?tenantId={TestTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrgUnit_AsSystemAdmin_Returns200()
    {
        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}?tenantId={TestTenantId}",
            new
            {
                Name = $"Test Dept {Guid.NewGuid():N}"[..20],
                NameEn = "Test Department",
                Code = $"DEPT-{Guid.NewGuid():N}"[..12],
                ParentId = (Guid?)null
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrgUnit_AsViewer_Returns403()
    {
        // Viewer does not have Admin/SystemAdmin role — must be forbidden
        using var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"]);

        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}?tenantId={TestTenantId}",
            new
            {
                Name = "Unauthorized Dept",
                NameEn = "Unauthorized Department",
                Code = "UNAUTH-001",
                ParentId = (Guid?)null
            });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrgUnitById_WithNonExistentId_Returns404()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await Client.GetAsync(
            $"{BaseUrl}/{nonExistentId}?tenantId={TestTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateThenGetOrgUnit_ReturnsCreatedUnit()
    {
        // Create an org unit
        var code = $"ORG-{Guid.NewGuid():N}"[..12];
        var createResponse = await Client.PostAsJsonAsync(
            $"{BaseUrl}?tenantId={TestTenantId}",
            new
            {
                Name = $"Integration Unit {code}",
                NameEn = "Integration Unit EN",
                Code = code,
                ParentId = (Guid?)null
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = body.GetProperty("data").GetProperty("id").GetGuid();

        // Fetch the created unit
        var getResponse = await Client.GetAsync(
            $"{BaseUrl}/{createdId}?tenantId={TestTenantId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrgTree_Unauthenticated_Returns401Or403()
    {
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync(
            $"{BaseUrl}?tenantId={TestTenantId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
