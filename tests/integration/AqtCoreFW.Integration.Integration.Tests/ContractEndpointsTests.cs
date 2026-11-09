using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Integration.Integration.Tests;

/// <summary>
/// Integration tests for Integration module REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// Covers: contracts and partners auth/response shape.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ContractEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    public ContractEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task ListContracts_Authenticated_Returns200()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/integration/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListContracts_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/integration/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListContracts_ResponseWrappedInApiEnvelope()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/integration/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ListPartners_Authenticated_Returns200()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/integration/partners");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
