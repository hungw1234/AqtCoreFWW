using System.Net;
using System.Text.Json;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;

namespace AqtCoreFW.Rules.Integration.Tests;

/// <summary>
/// Integration smoke tests for the Rules module endpoints.
/// DecisionTablesController has no list endpoint (only POST + GET {id}),
/// so list-style coverage uses RuleSetsController GET api/v1/rules/rule-sets.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DecisionTableEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();

    // Route: GET api/v1/rules/rule-sets (RuleSetsController.List)
    private const string ListRuleSetsRoute = "api/v1/rules/rule-sets";

    public DecisionTableEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>GET api/v1/rules/rule-sets with valid auth must return HTTP 200.</summary>
    [Fact]
    public async Task ListDecisionTables_Authenticated_Returns200()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Act
        var response = await client.GetAsync(ListRuleSetsRoute);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>GET api/v1/rules/rule-sets without auth headers must return HTTP 401.</summary>
    [Fact]
    public async Task ListDecisionTables_Unauthenticated_Returns401()
    {
        // Arrange — plain client, no auth headers
        using var client = _app.CreateClient();

        // Act
        var response = await client.GetAsync(ListRuleSetsRoute);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>GET api/v1/rules/rule-sets response body must be wrapped in the standard API envelope with a "success" property.</summary>
    [Fact]
    public async Task ListDecisionTables_ResponseWrappedInApiEnvelope()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Act
        var response = await client.GetAsync(ListRuleSetsRoute);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("success", out var successProp).Should().BeTrue(
            "response body must contain a top-level 'success' property (API envelope)");
        successProp.GetBoolean().Should().BeTrue();
    }
}
