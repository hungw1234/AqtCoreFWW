using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Integration tests for UsersAdminController — /api/v1/admin/users
/// Requires "Admin" policy (roles: Admin or SystemAdmin).
/// </summary>
[Collection("Integration")]
public class UsersApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/admin/users";

    // --- List users ---

    [Fact]
    public async Task ListUsers_AsSystemAdmin_Returns200()
    {
        var response = await Client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListUsers_AsAdmin_Returns200()
    {
        // tenantId required — ResolveTenantId() in ApiControllerBase throws UnauthorizedAccessException
        // (→ 401) when the JWT tenant claim is missing; see F-01 rule.
        using var client = CreateAuthenticatedClient(roles: ["Admin"], tenantId: DefaultTenantId.ToString());

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListUsers_WithoutAdminRole_Returns403()
    {
        // Authenticated but no Admin/SystemAdmin role — should be Forbidden
        using var client = CreateAuthenticatedClient(roles: ["GovOfficer"]);

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_Unauthenticated_Returns401Or403()
    {
        // No X-Test-* headers — TestAuthHandler still builds a principal without roles
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync(BaseUrl);

        // 401 if policy requires auth identity; 403 if scheme always authenticates but no role
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // --- Create user ---

    [Fact]
    public async Task CreateUser_WithValidData_Returns201()
    {
        var uniqueEmail = $"integration-{Guid.NewGuid():N}@test.vn";

        var response = await Client.PostAsJsonAsync(BaseUrl, new
        {
            FullName = "Integration Test User",
            Email = uniqueEmail,
            Password = "TestPass@123!Abc",
            DepartmentCode = (string?)null,
            TenantId = (Guid?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateUser_WithInvalidEmail_Returns422()
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, new
        {
            FullName = "Bad Email User",
            Email = "not-a-valid-email",
            Password = "TestPass@123!Abc",
            DepartmentCode = (string?)null,
            TenantId = (Guid?)null,
        });

        // FluentValidation pipeline returns 422 UnprocessableEntity
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateUser_WithWeakPassword_Returns422()
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, new
        {
            FullName = "Weak Password User",
            Email = $"weakpass-{Guid.NewGuid():N}@test.vn",
            Password = "weak",  // fails: min 12 chars, upper+lower+digit+special
            DepartmentCode = (string?)null,
            TenantId = (Guid?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateUser_WithMissingFullName_Returns422()
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, new
        {
            FullName = "",
            Email = $"nofullname-{Guid.NewGuid():N}@test.vn",
            Password = "TestPass@123!Abc",
            DepartmentCode = (string?)null,
            TenantId = (Guid?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // --- Get user by ID ---

    [Fact]
    public async Task GetUserById_WithNonExistentId_Returns404()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateThenGetUser_ReturnsCreatedUser()
    {
        var uniqueEmail = $"get-flow-{Guid.NewGuid():N}@test.vn";

        // Create
        var createResponse = await Client.PostAsJsonAsync(BaseUrl, new
        {
            FullName = "Get Flow User",
            Email = uniqueEmail,
            Password = "TestPass@123!Xyz",
            DepartmentCode = (string?)null,
            TenantId = (Guid?)null,
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Location header contains the new resource URL
        var location = createResponse.Headers.Location;
        location.Should().NotBeNull();

        // Fetch the created user via Location header
        var getResponse = await Client.GetAsync(location);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
