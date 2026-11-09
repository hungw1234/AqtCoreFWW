using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
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
/// Regression guard for AUDIT-INCIDENT-DTO-DAPPER (P1, 2026-05-08): SecurityIncidentDto
/// must materialize correctly via Dapper (positional record was failing on enum + DateTime
/// columns → 500). Verifies GET /api/v1/admin/incidents returns 200 + non-empty paged
/// envelope when at least one incident exists.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SecurityIncidentsApiTests(WebAppFixture app) : IClassFixture<WebAppFixture>
{
    private const string BaseUrl = "/api/v1/admin/incidents";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    [Fact]
    public async Task List_WithViewPermission_Returns200_AndMaterializesSeededIncident()
    {
        var seededId = await SeedIncidentAsync(TenantId, AuditSeverity.High, "Suspicious login burst");

        try
        {
            var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Incident.View);
            var response = await client.GetAsync($"{BaseUrl}?tenantId={TenantId}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("success").GetBoolean().Should().BeTrue();

            var data = body.GetProperty("data");
            var items = data.GetProperty("items");
            items.ValueKind.Should().Be(JsonValueKind.Array);

            // Diagnostic: print returned IDs on failure instead of opaque "Sequence contains no matching element".
            var ids = items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
            ids.Should().Contain(seededId, "seeded incident must round-trip via Dapper materialization");

            var seeded = items.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == seededId);
            // API uses JsonStringEnumConverter globally — enums serialize as their name, not number.
            seeded.GetProperty("severity").GetString().Should().Be(AuditSeverity.High.ToString());
            seeded.GetProperty("status").GetString().Should().Be(IncidentStatus.Open.ToString());
            seeded.GetProperty("title").GetString().Should().Be("Suspicious login burst");
            seeded.GetProperty("createdAt").GetString().Should().NotBeNullOrEmpty();
        }
        finally
        {
            await DeleteIncidentAsync(seededId);
        }
    }

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var client = app.CreateClient();
        var response = await client.GetAsync(BaseUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedIncidentAsync(Guid tenantId, AuditSeverity severity, string title)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var incident = SecurityIncident.Report(
            gen,
            tenantId,
            title,
            severity,
            "Seeded by integration test",
            reportedBy: AdminId,
            occurredAt: DateTimeOffset.UtcNow);
        db.SecurityIncidents.Add(incident);
        await db.SaveChangesAsync();
        return incident.MessageId;
    }

    private async Task DeleteIncidentAsync(Guid messageId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var rows = await db.SecurityIncidents
            .Where(i => i.MessageId == messageId)
            .ToListAsync();
        if (rows.Count == 0) return;
        db.SecurityIncidents.RemoveRange(rows);
        await db.SaveChangesAsync();
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
        // Controller has [RequiresMfa] — supply mfa_at claim.
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.MfaAtHeader,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }
}
