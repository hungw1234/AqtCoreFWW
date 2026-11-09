using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;

namespace AqtCoreFW.Collaboration.Integration.Tests;

[Collection(SqlServerCollection.CollectionName)]
public sealed class ConversationEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();

    public ConversationEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>GET api/v1/conversations with valid auth must return HTTP 200.</summary>
    [Fact]
    public async Task GetConversations_Authenticated_Returns200()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Act
        var response = await client.GetAsync("api/v1/conversations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>GET api/v1/conversations without auth headers must return HTTP 401.</summary>
    [Fact]
    public async Task GetConversations_Unauthenticated_Returns401()
    {
        // Arrange — plain client, no auth headers
        using var client = _app.CreateClient();

        // Act
        var response = await client.GetAsync("api/v1/conversations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>GET api/v1/conversations response body must be wrapped in the standard API envelope with a "success" property.</summary>
    [Fact]
    public async Task GetConversations_ResponseWrappedInApiEnvelope()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Act
        var response = await client.GetAsync("api/v1/conversations");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("success", out var successProp).Should().BeTrue(
            "response body must contain a top-level 'success' property (API envelope)");
        successProp.GetBoolean().Should().BeTrue();
    }

    /// <summary>POST api/v1/conversations with a valid body must return HTTP 200.</summary>
    [Fact]
    public async Task CreateConversation_ValidPayload_Returns200()
    {
        // Arrange
        using var client = _app.CreateAuthenticatedClient(TestUserId, TestTenantId);

        // Body matches CreateConversationBody(ConversationType Type, string? Name, IReadOnlyList<Guid> MemberUserIds)
        var payload = new
        {
            type = (int)ConversationType.Group,
            name = "Integration Test Channel",
            memberUserIds = new[] { Guid.NewGuid(), Guid.NewGuid() },
        };

        // Act
        var response = await client.PostAsJsonAsync("api/v1/conversations", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
