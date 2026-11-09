using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Ai.Integration.Tests;

/// <summary>
/// Integration tests for AI REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// Covers: model-profiles and prompt-templates read endpoints.
/// Chat/stream/ingest endpoints are excluded — they require an external LLM (Ollama).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class AiEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public AiEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    // --- GET /api/v1/ai/model-profiles ---

    [Fact]
    public async Task ListModelProfiles_Authenticated_Returns200()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/ai/model-profiles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListModelProfiles_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/ai/model-profiles");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- GET /api/v1/ai/prompt-templates ---

    [Fact]
    public async Task ListPromptTemplates_Authenticated_Returns200()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.GetAsync("/api/v1/ai/prompt-templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
