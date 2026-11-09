using System.Net;
using System.Text.Json;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;

namespace AqtCoreFW.SystemParams.Integration.Tests;

/// <summary>
/// Integration tests for SystemParams module endpoints.
/// Admin endpoints require policy-based auth (permission seeding needed for full tests).
/// Smoke tests: anonymous feature-flag endpoint + 401 checks on admin routes.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class AnnouncementEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();

    public AnnouncementEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>GET api/v1/feature-flags/{key} is [AllowAnonymous] — must return 200.</summary>
    [Fact]
    public async Task GetFeatureFlag_Anonymous_Returns200()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("api/v1/feature-flags/test-flag");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>GET api/v1/feature-flags/{key} response wraps in API envelope.</summary>
    [Fact]
    public async Task GetFeatureFlag_ResponseWrappedInApiEnvelope()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("api/v1/feature-flags/test-flag");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("success", out var successProp).Should().BeTrue(
            "response body must contain a top-level 'success' property (API envelope)");
        successProp.GetBoolean().Should().BeTrue();
    }

    /// <summary>GET api/v1/admin/announcements without auth must return 401.</summary>
    [Fact]
    public async Task GetAnnouncements_Unauthenticated_Returns401()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("api/v1/admin/announcements");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
