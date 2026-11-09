using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// QD 742 ss3.2.b - verifies AuditRetentionPoliciesController CRUD + permission gates.
/// Asserts: 401 anon / 403 missing perm / 200 with View / 204 with Manage / 409 stale RowVersion /
/// 400 deleting global default.
/// T1 entity: AuditRetentionPolicy.Id is long (DB-assigned bigint PK).
/// Routes use {id:long} — SeedPolicyAsync returns long.
/// DTO AuditRetentionPolicyDto.Id is long — list response JSON "id" is a number, not a Guid.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class AuditRetentionPolicyApiTests(WebAppFixture app) : IClassFixture<WebAppFixture>
{
    private const string BaseUrl = "/api/v1/audit/retention-policies";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    // GlobalDefaultPolicyId is long (T1 bigint PK) — the seeded global default row.
    // Migration M1 inserts it with IDENTITY seed; actual value is DB-assigned (1 typically).
    // Delete test uses this constant but route is {id:long} so this must be long.
    private const long GlobalDefaultPolicyId = 1L;

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var client = app.CreateClient();
        var response = await client.GetAsync(BaseUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_MissingViewPermission_Returns403()
    {
        var client = app.CreateAuthenticatedClient(AdminId, TenantId, "GovOfficer");
        var response = await client.GetAsync(BaseUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithViewPermission_Returns200_WithApiResponseEnvelope()
    {
        // Note: TruncateAllDataAsync restores the migration-embedded global default after
        // wiping (ReseedMigrationEmbeddedDataAsync), so the seeded row IS present here;
        // AuditLogImmutabilityTests.GlobalDefaultRetentionPolicy_RowExists asserts it.
        // This test verifies the endpoint contract: 200 + ApiResponse envelope + array body.
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Retention.View);
        var response = await client.GetAsync(BaseUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Create_WithoutManage_Returns403()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Retention.View);
        var body = new
        {
            tenantId = (Guid?)TenantId,
            category = (int?)AuditCategory.AdminLogin,
            retentionDays = 90,
            archiveBeforePurge = false,
            archivePath = (string?)null
        };
        var response = await client.PostAsJsonAsync(BaseUrl, body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithManage_Returns200_AndPersistsRow()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Audit.Retention.View, PermCodes.Audit.Retention.Manage);

        var body = new
        {
            tenantId = (Guid?)TenantId,
            category = (int?)AuditCategory.Error,
            retentionDays = 730,
            archiveBeforePurge = false,
            archivePath = (string?)null
        };
        var response = await client.PostAsJsonAsync(BaseUrl, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create handler returns Result<long> — response data is a long (bigint PK).
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newId = envelope.GetProperty("data").GetInt64();
        newId.Should().BeGreaterThan(0);

        // Verify persisted via DbContext PK lookup (long).
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var row = await db.AuditRetentionPolicies.FindAsync(newId);
        row.Should().NotBeNull();
        row!.RetentionDays.Should().Be(730);
        row.Category.Should().Be(AuditCategory.Error);
    }

    [Fact]
    public async Task Update_StaleRowVersion_Returns409()
    {
        var policyId = await SeedPolicyAsync(TenantId, AuditCategory.AppAccess, 60);

        var client = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Audit.Retention.View, PermCodes.Audit.Retention.Manage);

        // Use a deliberately bogus rowversion (8 zero bytes -- guaranteed stale)
        var staleVersion = Convert.ToBase64String(new byte[8]);
        var body = new { retentionDays = 90, archiveBeforePurge = false, archivePath = (string?)null, rowVersion = staleVersion };
        var response = await client.PutAsJsonAsync($"{BaseUrl}/{policyId}", body);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_FreshRowVersion_Returns204_AndPersistsChange()
    {
        var policyId = await SeedPolicyAsync(TenantId, AuditCategory.ConfigChange, 60);

        var client = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Audit.Retention.View, PermCodes.Audit.Retention.Manage);

        // Get current rowversion via list endpoint.
        // DTO.Id is long -- deserialize as Int64, not Guid.
        var listResp = await client.GetAsync(BaseUrl);
        var rows = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray()
            .Select(e => new { Id = e.GetProperty("id").GetInt64(), RowVersion = e.GetProperty("rowVersion").GetString() })
            .ToList();
        var rowVersion = rows.First(r => r.Id == policyId).RowVersion!;

        var body = new { retentionDays = 120, archiveBeforePurge = false, archivePath = (string?)null, rowVersion };
        var response = await client.PutAsJsonAsync($"{BaseUrl}/{policyId}", body);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var row = await db.AuditRetentionPolicies.FindAsync(policyId);
        row!.RetentionDays.Should().Be(120);
    }

    [Fact]
    public async Task Update_AnotherTenantsPolicy_NonSystemAdmin_Returns403()
    {
        // Code-review H1+H2 regression guard: a tenant user with RETENTION.MANAGE must not
        // be able to PUT a policy that belongs to a different tenant.
        var otherTenantId = Guid.NewGuid();
        var policyId = await SeedPolicyAsync(otherTenantId, AuditCategory.AppAccess, 90);

        var client = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Audit.Retention.View, PermCodes.Audit.Retention.Manage);

        // Need the real RowVersion so the test fails on the tenant guard, not on stale-row.
        var listResp = await client.GetAsync(BaseUrl);
        listResp.EnsureSuccessStatusCode();
        // Use a bogus rowversion -- the trust-boundary check fires BEFORE the rowversion compare.
        var body = new
        {
            retentionDays = 30,
            archiveBeforePurge = false,
            archivePath = (string?)null,
            rowVersion = Convert.ToBase64String(new byte[8])
        };
        var response = await client.PutAsJsonAsync($"{BaseUrl}/{policyId}", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_GlobalPolicy_NonSystemAdmin_Returns403()
    {
        // Code-review H2 regression guard: tenant user must not mutate global (TenantId=null) policies.
        var policyId = await SeedPolicyAsync(tenantId: null, AuditCategory.AdminLogin, 365);

        var client = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Audit.Retention.View, PermCodes.Audit.Retention.Manage);

        var body = new
        {
            retentionDays = 30,
            archiveBeforePurge = false,
            archivePath = (string?)null,
            rowVersion = Convert.ToBase64String(new byte[8])
        };
        var response = await client.PutAsJsonAsync($"{BaseUrl}/{policyId}", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_GlobalDefault_Returns400()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Audit.Retention.View, PermCodes.Audit.Retention.Manage);

        // Even with a "valid-shaped" rowversion, the global default ID is short-circuit rejected.
        // Route is {id:long} — GlobalDefaultPolicyId is long.
        var fakeRowVersion = Convert.ToBase64String(new byte[8]);
        var response = await client.DeleteAsync(
            $"{BaseUrl}/{GlobalDefaultPolicyId}?rowVersion={Uri.EscapeDataString(fakeRowVersion)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- Helpers ---

    /// <summary>
    /// Seeds an AuditRetentionPolicy row directly via DbContext.
    /// Returns the long PK (T1 entity: AuditRetentionPolicy.Id is bigint DB-assigned).
    /// </summary>
    private async Task<long> SeedPolicyAsync(Guid? tenantId, AuditCategory? category, int retentionDays)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var policy = AuditRetentionPolicy.Create(tenantId, category, retentionDays);
        db.AuditRetentionPolicies.Add(policy);
        await db.SaveChangesAsync();
        return policy.Id; // T1: long PK (DB-assigned bigint identity)
    }

    private HttpClient CreateClientWithPermissions(Guid userId, Guid tenantId, params string[] permissions)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string>(permissions)
            });

        var factory = app.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEffectivePermissionService>();
            services.AddSingleton(permService);
        }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "GovOfficer");
        return client;
    }
}
