using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Identity.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for TenantsAdminController.
/// Tests HTTP roundtrip via WebApplicationFactory against real SQL Server.
/// WebAppFixture bypasses JWT validation via TestAuthHandler — uses headers for auth claims.
///
/// Tenant is T2: long PK (Id, DB-assigned) + Guid AK (PublicId).
/// API routes use PublicId (Guid). Tests capture tenant.PublicId after SaveChanges.
/// db.Tenants.FindAsync is replaced with FirstOrDefaultAsync(t => t.PublicId == ...) since PK is long.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class TenantsAdminControllerTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid SystemAdminUserId = Guid.NewGuid();
    private static readonly Guid RegularUserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    // Permission constants for header injection
    private const string TenantViewPermission = "IDENTITY.TENANT.VIEW";
    private const string TenantManagePermission = "IDENTITY.TENANT.MANAGE";

    // Shared IGuidGenerator stub — Tenant.Create requires IGuidGenerator (T2 refactor B2)
    private readonly IGuidGenerator _guidGen;

    public TenantsAdminControllerTests(WebAppFixture app)
    {
        _app = app;
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    private HttpClient CreateSystemAdminClient()
    {
        // TestAuthHandler reads claims from headers — SystemAdmin role grants all permissions
        // Phase-03: Include MFA claim to bypass [RequiresMfa] attribute on TenantsAdminController
        return _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: true, "SystemAdmin");
    }

    private HttpClient CreateRegularUserClient()
    {
        // No special roles — lacks IDENTITY.TENANT.* permissions
        // Phase-03: Include MFA claim to bypass [RequiresMfa] attribute on TenantsAdminController
        return _app.CreateAuthenticatedClient(RegularUserId, TenantId, includeMfaClaim: true);
    }

    private HttpClient CreateAnonymousClient()
    {
        return _app.CreateClient();
    }

    // --- GET /api/v1/admin/tenants ---

    [Fact]
    public async Task List_SystemAdmin_Returns200()
    {
        var client = CreateSystemAdminClient();

        var response = await client.GetAsync("/api/v1/admin/tenants");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/tenants");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_RegularUser_Returns403()
    {
        var client = CreateRegularUserClient();

        var response = await client.GetAsync("/api/v1/admin/tenants");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // --- POST /api/v1/admin/tenants ---

    [Fact]
    public async Task Create_ValidRequest_Returns201()
    {
        var client = CreateSystemAdminClient();
        var uniqueCode = $"TEST{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var request = new
        {
            Name = "Test Tenant",
            Code = uniqueCode,
            ContactEmail = "test@example.com"
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = response.Headers.Location?.ToString();
        location.Should().Contain("/api/v1/admin/tenants/");
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns409()
    {
        // First create a tenant
        var client = CreateSystemAdminClient();
        var code = $"DUP{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        // Seed directly to DB — Tenant.Create requires IGuidGenerator (T2 refactor B2)
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = Tenant.Create(_guidGen, "Existing", code, null, SystemAdminUserId);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        // Try to create another with same code
        var request = new { Name = "New Tenant", Code = code, ContactEmail = (string?)null };
        var response = await client.PostAsJsonAsync("/api/v1/admin/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();
        var request = new { Name = "Test", Code = "ANON", ContactEmail = (string?)null };

        var response = await client.PostAsJsonAsync("/api/v1/admin/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- GET /api/v1/admin/tenants/{id} ---

    [Fact]
    public async Task GetById_ExistingTenant_Returns200()
    {
        var client = CreateSystemAdminClient();
        // T2: routes use PublicId (Guid AK), not long PK (Id)
        Guid tenantPublicId;

        // Seed a tenant — capture PublicId after SaveChanges (DB assigns long Id)
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = Tenant.Create(_guidGen, "Get Test", $"GET{Guid.NewGuid():N}"[..8], null, SystemAdminUserId);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantPublicId = tenant.PublicId;
        }

        var response = await client.GetAsync($"/api/v1/admin/tenants/{tenantPublicId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Response is wrapped in ApiResponse<T> envelope: { "data": {...}, ... }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("id").GetGuid().Should().Be(tenantPublicId);
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        var client = CreateSystemAdminClient();
        var nonExistentId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/admin/tenants/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- PUT /api/v1/admin/tenants/{id} ---

    [Fact]
    public async Task Update_ExistingTenant_Returns204()
    {
        var client = CreateSystemAdminClient();
        Guid tenantPublicId;

        // Seed a tenant
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = Tenant.Create(_guidGen, "Original", $"UPD{Guid.NewGuid():N}"[..8], null, SystemAdminUserId);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantPublicId = tenant.PublicId;
        }

        var request = new { Name = "Updated Name", ContactEmail = "updated@example.com" };
        var response = await client.PutAsJsonAsync($"/api/v1/admin/tenants/{tenantPublicId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update — query by PublicId (Guid AK) since Id is long (bigint identity)
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.PublicId == tenantPublicId);
            tenant!.Name.Should().Be("Updated Name");
            tenant.ContactEmail.Should().Be("updated@example.com");
        }
    }

    // --- POST /api/v1/admin/tenants/{id}/status ---

    [Fact]
    public async Task ChangeStatus_ToSuspended_Returns204()
    {
        var client = CreateSystemAdminClient();
        Guid tenantPublicId;

        // Seed a tenant
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = Tenant.Create(_guidGen, "Status Test", $"STS{Guid.NewGuid():N}"[..8], null, SystemAdminUserId);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantPublicId = tenant.PublicId;
        }

        var request = new { Status = TenantStatus.Suspended };
        var response = await client.PostAsJsonAsync($"/api/v1/admin/tenants/{tenantPublicId}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify status change
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.PublicId == tenantPublicId);
            tenant!.Status.Should().Be(TenantStatus.Suspended);
        }
    }

    // --- DELETE /api/v1/admin/tenants/{id} ---

    [Fact]
    public async Task Delete_SoftDeletesAsArchived_Returns204()
    {
        var client = CreateSystemAdminClient();
        Guid tenantPublicId;

        // Seed a tenant
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = Tenant.Create(_guidGen, "Delete Test", $"DEL{Guid.NewGuid():N}"[..8], null, SystemAdminUserId);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantPublicId = tenant.PublicId;
        }

        var response = await client.DeleteAsync($"/api/v1/admin/tenants/{tenantPublicId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify soft-deleted (Status = Archived)
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.PublicId == tenantPublicId);
            tenant!.Status.Should().Be(TenantStatus.Archived);
        }
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var client = CreateSystemAdminClient();

        var response = await client.DeleteAsync($"/api/v1/admin/tenants/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
