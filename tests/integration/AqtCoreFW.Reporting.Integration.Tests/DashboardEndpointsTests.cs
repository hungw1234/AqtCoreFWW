using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;

namespace AqtCoreFW.Reporting.Integration.Tests;

[Collection(SqlServerCollection.CollectionName)]
public sealed class DashboardEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();

    public DashboardEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>GET api/v1/dashboards with valid auth must return HTTP 200.</summary>
    [Fact]
    public async Task ListDashboards_Authenticated_Returns200()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Act
        var response = await client.GetAsync("api/v1/dashboards");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>GET api/v1/dashboards without auth headers must return HTTP 401.</summary>
    [Fact]
    public async Task ListDashboards_Unauthenticated_Returns401()
    {
        // Arrange — plain client, no auth headers
        using var client = _app.CreateClient();

        // Act
        var response = await client.GetAsync("api/v1/dashboards");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>GET api/v1/dashboards response body must be wrapped in the standard API envelope with a "success" property.</summary>
    [Fact]
    public async Task ListDashboards_ResponseWrappedInApiEnvelope()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Act
        var response = await client.GetAsync("api/v1/dashboards");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("success", out var successProp).Should().BeTrue(
            "response body must contain a top-level 'success' property (API envelope)");
        successProp.GetBoolean().Should().BeTrue();
    }

    /// <summary>POST api/v1/dashboards with a valid body must return HTTP 200.</summary>
    [Fact]
    public async Task CreateDashboard_ValidPayload_Returns200()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Body matches CreateDashboardRequest(string Name, bool? IsDefault)
        var payload = new
        {
            name = "Integration Test Dashboard",
            isDefault = false,
        };

        // Act
        var response = await client.PostAsJsonAsync("api/v1/dashboards", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
