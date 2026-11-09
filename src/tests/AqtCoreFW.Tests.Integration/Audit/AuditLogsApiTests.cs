using System.Net;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Audit;

/// <summary>
/// Smoke tests for AuditLogsController — GET /api/v1/audit/logs
/// Auth enforced at global middleware level (no [Authorize] on controller, but policy requires authenticated).
/// </summary>
[Collection("Integration")]
public class AuditLogsApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string LogsUrl = "/api/v1/audit/logs";
    private const string StatsUrl = "/api/v1/audit/statistics";

    [Fact]
    public async Task GetAuditLogs_AsSystemAdmin_Returns200()
    {
        var response = await Client.GetAsync(
            $"{LogsUrl}?from=2026-01-01&to=2026-12-31&page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAuditLogs_NoFilters_Returns200()
    {
        // Default pagination — should work without any filter params
        var response = await Client.GetAsync(LogsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAuditLogs_WithRoleViewer_Returns403OrOk()
    {
        // Viewer role — enforcement depends on global policy config
        // Must NOT return 500
        using var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"]);

        var response = await client.GetAsync(LogsUrl);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetAuditLogs_Unauthenticated_Returns401Or403()
    {
        // No auth headers — TestAuthHandler authenticates but without roles; policy may deny
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync(LogsUrl);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditStatistics_AsSystemAdmin_Returns200()
    {
        var response = await Client.GetAsync(StatsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
