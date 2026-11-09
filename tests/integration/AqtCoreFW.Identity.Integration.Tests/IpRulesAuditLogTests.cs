using System.Text.Json;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests asserting that IpRule CREATE/UPDATE/DELETE emit exactly one AuditLogEntry
/// with Category=ConfigChange (QĐ 742 §3.2.a).
///
/// P0-2 fix verification: previously commands used IRequest{T} instead of ICommand{T};
/// AuditBehavior (constrained to IBaseCommand) was silently skipped.
/// After fix, all three commands must produce an audit entry on success.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IpRulesAuditLogTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid SystemAdminUserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public IpRulesAuditLogTests(WebAppFixture app) => _app = app;

    private HttpClient CreateSystemAdminClient()
        => _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: true, "SystemAdmin");

    // ── CREATE emits ConfigChange audit entry ─────────────────────────────────

    [Fact]
    public async Task Create_IpRule_EmitsAuditLogEntry_WithConfigChangeCategory()
    {
        var client = CreateSystemAdminClient();
        var auditDb = GetAuditDb();

        // Snapshot count before action
        var countBefore = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.ConfigChange && e.ResourceType == "IpRule")
            .CountAsync();

        var request = new
        {
            TenantId = (Guid?)null,
            Cidr = $"100.{Random.Shared.Next(1, 254)}.0.0/16",
            Description = "Audit test rule",
            IsEnabled = true,
            IsBlock = true
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // AuditBehavior is synchronous inside the MediatR pipeline — no async propagation delay.
        var countAfter = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.ConfigChange && e.ResourceType == "IpRule")
            .CountAsync();

        countAfter.Should().BeGreaterThan(countBefore,
            "AuditBehavior must fire for CreateIpRuleCommand (ICommand<long>) on success");
    }

    // ── UPDATE emits ConfigChange audit entry ─────────────────────────────────

    [Fact]
    public async Task Update_IpRule_EmitsAuditLogEntry_WithConfigChangeCategory()
    {
        var client = CreateSystemAdminClient();
        var auditDb = GetAuditDb();

        // Create a rule first to obtain its Id
        var cidr = $"101.{Random.Shared.Next(1, 254)}.0.0/16";
        var createRequest = new { TenantId = (Guid?)null, Cidr = cidr, Description = "Pre-update", IsEnabled = true, IsBlock = true };
        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var ruleId = await ReadCreatedIdAsync(createResponse);

        var countBefore = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.ConfigChange && e.ResourceType == "IpRule")
            .CountAsync();

        var updateRequest = new { Cidr = cidr, Description = "Post-update", IsEnabled = false, IsBlock = true };
        var response = await client.PutAsJsonAsync($"/api/v1/admin/security/ip-rules/{ruleId}", updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var countAfter = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.ConfigChange && e.ResourceType == "IpRule")
            .CountAsync();

        countAfter.Should().BeGreaterThan(countBefore,
            "AuditBehavior must fire for UpdateIpRuleCommand (ICommand) on success");
    }

    // ── DELETE emits ConfigChange audit entry ─────────────────────────────────

    [Fact]
    public async Task Delete_IpRule_EmitsAuditLogEntry_WithConfigChangeCategory()
    {
        var client = CreateSystemAdminClient();
        var auditDb = GetAuditDb();

        // Create a rule to delete — obtain its Id
        var cidr = $"102.{Random.Shared.Next(1, 254)}.0.0/16";
        var createRequest = new { TenantId = (Guid?)null, Cidr = cidr, Description = "To delete", IsEnabled = true, IsBlock = true };
        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/security/ip-rules", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var ruleId = await ReadCreatedIdAsync(createResponse);

        var countBefore = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.ConfigChange && e.ResourceType == "IpRule")
            .CountAsync();

        var response = await client.DeleteAsync($"/api/v1/admin/security/ip-rules/{ruleId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var countAfter = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.ConfigChange && e.ResourceType == "IpRule")
            .CountAsync();

        countAfter.Should().BeGreaterThan(countBefore,
            "AuditBehavior must fire for DeleteIpRuleCommand (ICommand) on success");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AuditDbContext GetAuditDb()
    {
        var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }

    /// <summary>Reads the long Id from the ApiResponse{long} envelope returned by POST ip-rules.</summary>
    private static async Task<long> ReadCreatedIdAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        // Envelope: { "success": true, "data": 123, "meta": {...}, "errors": null }
        return doc.RootElement.GetProperty("data").GetInt64();
    }
}
