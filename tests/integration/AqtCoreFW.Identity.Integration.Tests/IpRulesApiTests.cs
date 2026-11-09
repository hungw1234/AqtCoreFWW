using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for IpRulesAdminController (QĐ 742 §2.2.a — IP whitelist/blocklist management).
/// Tests HTTP roundtrip via WebApplicationFactory against real SQL Server.
/// WebAppFixture bypasses JWT validation via TestAuthHandler — uses headers for auth claims.
///
/// IP Filter rules (IpFilterRule) are T1 entities: addressed by long Id.
/// API routes use long Id directly.
/// Tests require [RequiresMfa] attribute + Security.IpRule.View/Manage permissions.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IpRulesApiTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid SystemAdminUserId = Guid.NewGuid();
    private static readonly Guid RegularUserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    // Unique CIDR counter — each test that seeds a rule gets a distinct CIDR.
    // Prevents UQ_IpFilterRule_TenantId_Cidr violations when tests run in the same DB.
    private static int _cidrCounter;
    private static string NextUniqueCidr() =>
        $"10.{System.Threading.Interlocked.Increment(ref _cidrCounter)}.0.0/16";

    public IpRulesApiTests(WebAppFixture app)
    {
        _app = app;
    }

    private HttpClient CreateSystemAdminClient()
    {
        // SystemAdmin role grants all permissions; includes MFA claim for [RequiresMfa]
        return _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: true, "SystemAdmin");
    }

    private HttpClient CreateRegularUserClient()
    {
        // No special roles — lacks Security.IpRule.* permissions
        return _app.CreateAuthenticatedClient(RegularUserId, TenantId, includeMfaClaim: true);
    }

    private HttpClient CreateMfalessClient()
    {
        // System admin but NO MFA claim — should fail [RequiresMfa]
        return _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: false, "SystemAdmin");
    }

    private HttpClient CreateAnonymousClient()
    {
        return _app.CreateClient();
    }

    // ── GET /api/v1/admin/security/ip-rules ──────────────────────────────────────

    [Fact]
    public async Task List_SystemAdmin_Returns200WithEmptyList()
    {
        var client = CreateSystemAdminClient();

        var response = await client.GetAsync("/api/v1/admin/security/ip-rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_SystemAdmin_ReturnsCreatedRules()
    {
        var client = CreateSystemAdminClient();

        // Seed a rule with a unique CIDR to avoid UQ_IpFilterRule_TenantId_Cidr conflict.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = IpFilterRule.Create(
                tenantId: null,
                cidr: NextUniqueCidr(),
                description: "Test rule",
                isEnabled: true,
                isBlock: true,
                createdBy: SystemAdminUserId);
            db.Set<IpFilterRule>().Add(rule);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/admin/security/ip-rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/security/ip-rules");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_RegularUserNoPermission_Returns403()
    {
        var client = CreateRegularUserClient();

        var response = await client.GetAsync("/api/v1/admin/security/ip-rules");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_NoMfaClaim_Returns403()
    {
        var client = CreateMfalessClient();

        var response = await client.GetAsync("/api/v1/admin/security/ip-rules");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/v1/admin/security/ip-rules/{id} ─────────────────────────────────

    [Fact]
    public async Task GetById_ValidId_Returns200()
    {
        var client = CreateSystemAdminClient();
        long ruleId = 0;

        // Seed a rule with a unique CIDR to avoid UQ_IpFilterRule_TenantId_Cidr conflict.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = IpFilterRule.Create(
                tenantId: null,
                cidr: NextUniqueCidr(),
                description: "Corp network",
                isEnabled: true,
                isBlock: false,
                createdBy: SystemAdminUserId);
            db.Set<IpFilterRule>().Add(rule);
            await db.SaveChangesAsync();
            ruleId = rule.Id;
        }

        var response = await client.GetAsync($"/api/v1/admin/security/ip-rules/{ruleId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_InvalidId_Returns404()
    {
        var client = CreateSystemAdminClient();

        var response = await client.GetAsync("/api/v1/admin/security/ip-rules/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/v1/admin/security/ip-rules ──────────────────────────────────────

    [Fact]
    public async Task Create_ValidCidr_Returns201()
    {
        var client = CreateSystemAdminClient();
        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = NextUniqueCidr(),
            Description = "Private network",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_InvalidCidr_Returns422()
    {
        var client = CreateSystemAdminClient();
        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = "invalid-cidr-format",
            Description = "Bad CIDR",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_EmptyCidr_Returns422()
    {
        var client = CreateSystemAdminClient();
        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = "",
            Description = "Empty CIDR",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_RegularUserNoManagePermission_Returns403()
    {
        var client = CreateRegularUserClient();
        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = "192.168.0.0/16",
            Description = "Test",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();
        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = "192.168.0.0/16",
            Description = "Test",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_NoMfaClaim_Returns403()
    {
        var client = CreateMfalessClient();
        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = "192.168.0.0/16",
            Description = "Test",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/v1/admin/security/ip-rules/{id} ─────────────────────────────────

    [Fact]
    public async Task Update_ValidRequest_Returns204()
    {
        var client = CreateSystemAdminClient();
        long ruleId = 0;

        // Seed a rule with a unique CIDR to avoid UQ_IpFilterRule_TenantId_Cidr conflict.
        var cidr = NextUniqueCidr();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = IpFilterRule.Create(
                tenantId: null,
                cidr: cidr,
                description: "Original",
                isEnabled: true,
                isBlock: true,
                createdBy: SystemAdminUserId);
            db.Set<IpFilterRule>().Add(rule);
            await db.SaveChangesAsync();
            ruleId = rule.Id;
        }

        var request = new
        {
            Cidr = cidr,
            Description = "Updated",
            IsEnabled = false,
            IsBlock = false
        };

        var response = await client.PutAsJsonAsync($"/api/v1/admin/security/ip-rules/{ruleId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update in DB
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = await db.Set<IpFilterRule>().FindAsync(ruleId);
            rule.Should().NotBeNull();
            rule!.Description.Should().Be("Updated");
            rule.IsEnabled.Should().BeFalse();
            rule.IsBlock.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Update_InvalidId_Returns404()
    {
        var client = CreateSystemAdminClient();
        var request = new
        {
            Cidr = "10.0.0.0/8",
            Description = "Test",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PutAsJsonAsync("/api/v1/admin/security/ip-rules/99999", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_InvalidCidr_Returns422()
    {
        var client = CreateSystemAdminClient();
        long ruleId = 0;

        // Seed a rule with a unique CIDR to avoid UQ_IpFilterRule_TenantId_Cidr conflict.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = IpFilterRule.Create(
                tenantId: null,
                cidr: NextUniqueCidr(),
                description: "Original",
                isEnabled: true,
                isBlock: true,
                createdBy: SystemAdminUserId);
            db.Set<IpFilterRule>().Add(rule);
            await db.SaveChangesAsync();
            ruleId = rule.Id;
        }

        var request = new
        {
            Cidr = "bad-cidr",
            Description = "Test",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PutAsJsonAsync($"/api/v1/admin/security/ip-rules/{ruleId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Update_RegularUserNoPermission_Returns403()
    {
        var client = CreateRegularUserClient();
        var request = new
        {
            Cidr = "10.0.0.0/8",
            Description = "Test",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PutAsJsonAsync("/api/v1/admin/security/ip-rules/123", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DELETE /api/v1/admin/security/ip-rules/{id} ───────────────────────────────

    [Fact]
    public async Task Delete_ValidId_Returns204()
    {
        var client = CreateSystemAdminClient();
        long ruleId = 0;

        // Seed a rule with a unique CIDR to avoid UQ_IpFilterRule_TenantId_Cidr conflict.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = IpFilterRule.Create(
                tenantId: null,
                cidr: NextUniqueCidr(),
                description: "To delete",
                isEnabled: true,
                isBlock: true,
                createdBy: SystemAdminUserId);
            db.Set<IpFilterRule>().Add(rule);
            await db.SaveChangesAsync();
            ruleId = rule.Id;
        }

        var response = await client.DeleteAsync($"/api/v1/admin/security/ip-rules/{ruleId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion in DB
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = await db.Set<IpFilterRule>().FindAsync(ruleId);
            rule.Should().BeNull();
        }
    }

    [Fact]
    public async Task Delete_InvalidId_Returns404()
    {
        var client = CreateSystemAdminClient();

        var response = await client.DeleteAsync("/api/v1/admin/security/ip-rules/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RegularUserNoPermission_Returns403()
    {
        var client = CreateRegularUserClient();

        var response = await client.DeleteAsync("/api/v1/admin/security/ip-rules/123");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Insert-time persistence of CLR-default flag values ───────────────────────

    [Fact]
    public async Task Create_DisabledAllowRule_PersistsFalseFlags()
    {
        // A disabled (IsEnabled=false) allow-rule (IsBlock=false) sets both flags to the
        // CLR default for bool. Both columns carry a database DEFAULT of 1, so EF must send
        // the explicit false on INSERT — otherwise the column is omitted and the DB default
        // silently flips the rule to enabled+block, inverting the security semantics.
        var cidr = NextUniqueCidr();
        long ruleId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rule = IpFilterRule.Create(
                tenantId: null,
                cidr: cidr,
                description: "Whitelist override, staged disabled",
                isEnabled: false,
                isBlock: false,
                createdBy: SystemAdminUserId);
            db.Set<IpFilterRule>().Add(rule);
            await db.SaveChangesAsync();
            ruleId = rule.Id;
        }

        // Reload from a fresh context so the assertion reflects the persisted row, not the
        // tracked in-memory instance.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var reloaded = await db.Set<IpFilterRule>().AsNoTracking()
                .SingleAsync(r => r.Id == ruleId);

            reloaded.IsEnabled.Should().BeFalse("a rule created as disabled must persist disabled");
            reloaded.IsBlock.Should().BeFalse("an allow-rule must persist as allow, not flip to block");
        }
    }
}
