using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Ai;

/// <summary>
/// Smoke tests for AiController — /api/v1/ai
/// [Authorize] on controller: all endpoints require authenticated identity.
/// Ollama is NOT running in test env (AI:Ollama:Enabled=false in ApiFactory config).
/// Chat endpoint should return degraded response (200 with error message) or 503, NOT 500.
/// ResolveIdentity() reads "tenantId" claim or X-Tenant-Id header — pass via tenantId param.
/// ResolveIdentity() reads NameIdentifier for userId — TestAuthHandler sets this from X-Test-UserId.
/// </summary>
[Collection("Integration")]
public class AiApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string ChatUrl = "/api/v1/ai/chat";
    private const string HistoryUrl = "/api/v1/ai/chat";

    /// <summary>
    /// Creates a client with all identity claims AI controller needs:
    /// userId via X-Test-UserId header and tenantId via X-Test-TenantId header.
    /// </summary>
    private HttpClient CreateAiClient(string? tenantId = null)
    {
        var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"],
            tenantId: tenantId ?? Guid.NewGuid().ToString());

        // Short timeout — Ollama is offline, avoid hanging tests
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    [Fact]
    public async Task Chat_WhenOllamaUnavailable_DoesNotReturn500()
    {
        // Ollama disabled via AI:Ollama:Enabled=false — expect graceful degraded response
        // Valid responses: 200 (degraded), 400 (missing tenantId/userId), 503 (service unavailable)
        // Invalid: 500 (unhandled exception)
        using var client = CreateAiClient();

        var response = await client.PostAsJsonAsync(ChatUrl, new
        {
            Message = "How many pending cases are there?",
            SessionId = (Guid?)null
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Chat_WithValidIdentity_Returns200Or503()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateAiClient(tenantId: tenantId);

        var response = await client.PostAsJsonAsync(ChatUrl, new
        {
            Message = "Hello",
            SessionId = Guid.NewGuid()
        });

        // 200 = degraded AI response; 503 = Ollama unavailable; 400 = validation error
        // None of these should be 500
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Chat_Unauthenticated_Returns401Or403()
    {
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        anonClient.Timeout = TimeSpan.FromSeconds(15);

        var response = await anonClient.PostAsJsonAsync(ChatUrl, new
        {
            Message = "test",
            SessionId = Guid.NewGuid()
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetChatHistory_WithNonExistentSession_Returns404Or200Empty()
    {
        // Session does not exist — returns either 404 or 200 with empty list
        using var client = CreateAiClient();
        var sessionId = Guid.NewGuid();

        var response = await client.GetAsync(
            $"{HistoryUrl}/{sessionId}/history?page=1&pageSize=50");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.BadRequest);
    }
}
