using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.MasterData.Integration.Tests;

/// <summary>
/// Integration tests for MasterData Dictionary REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// Route: GET /api/v1/dictionaries
/// Auth: [Authorize] on controller — any authenticated user can list; writes require Master.Dict.Manage policy.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DictionaryEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.Parse("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId   = Guid.Parse("B0000000-0000-0000-0000-000000000001");

    public DictionaryEndpointsTests(WebAppFixture app) => _app = app;

    // ── TC-DICT-INT-001: Authenticated user receives 200 ────────────────────────

    [Fact]
    public async Task ListDictionaries_Authenticated_Returns200()
    {
        // Tenant resolves from X-Test-TenantId header → tenant_id claim → ITenantContext.
        // No query param needed — DictionaryController.List calls ResolveTenantId() from claims.
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/dictionaries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TC-DICT-INT-002: Unauthenticated request is rejected ────────────────────

    [Fact]
    public async Task ListDictionaries_Unauthenticated_Returns401()
    {
        // CreateClient() produces a plain client with no X-Test-* headers → no auth ticket.
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/dictionaries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TC-DICT-INT-003: Response is wrapped in the standard ApiResponse envelope ─

    [Fact]
    public async Task ListDictionaries_ResponseWrappedInApiEnvelope()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/dictionaries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ApiResponse<T> always contains a top-level "success" boolean.
        body.TryGetProperty("success", out var successProp).Should().BeTrue(
            "response must be wrapped in the standard ApiResponse envelope with a 'success' property");
        successProp.GetBoolean().Should().BeTrue(
            "'success' must be true for a 200 OK list response");
    }
}
