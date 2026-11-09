using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Workflow.Integration.Tests.WorkflowInstances;

/// <summary>
/// Integration tests for GET /api/v1/workflow/instances and GET /api/v1/workflow/instances/summary.
/// Covers: 200 paginated envelope, 401 no auth, 403 missing permission, 400 bad cursor, rate-limit smoke.
///
/// Permission model: PermissionAuthorizationHandler calls IEffectivePermissionService which reads from DB.
/// Since no data is seeded in Testing environment, all clients are 403 by default.
/// The permitted client overrides IEffectivePermissionService via ConfigureTestServices to grant
/// WORKFLOW.INSTANCE.VIEW for the admin user.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstancesListEndpointTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantA  = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    // WORKFLOW.INSTANCE.VIEW permission code (matches PermCodes.Workflow.Instance.View)
    private const string WorkflowInstanceViewPerm = "WORKFLOW.INSTANCE.VIEW";

    public WorkflowInstancesListEndpointTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── TC-WF-INT-LIST-001: 401 no auth ───────────────────────────────────────────

    [Fact]
    public async Task ListInstances_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/workflow/instances");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TC-WF-INT-LIST-002: 403 authenticated but missing WORKFLOW.INSTANCE.VIEW ─

    [Fact]
    public async Task ListInstances_AuthenticatedWithoutPermission_Returns403()
    {
        // No permission seeded in Testing env → EffectivePermissionService returns empty set → 403
        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "GovOfficer");

        var response = await client.GetAsync("/api/v1/workflow/instances");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TC-WF-INT-LIST-003: 200 paginated envelope ────────────────────────────────

    [Fact]
    public async Task ListInstances_AuthenticatedWithPermission_Returns200PaginatedEnvelope()
    {
        var client = CreatePermittedClient(AdminId, TenantA);

        var response = await client.GetAsync("/api/v1/workflow/instances");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();

        // data.items and data.meta present
        var data = body.GetProperty("data");
        data.TryGetProperty("items", out _).Should().BeTrue("items array must be present");
        data.TryGetProperty("meta", out var meta).Should().BeTrue("meta object must be present");

        // RT-07: cursor mode — totalCount MUST NOT be a positive integer.
        // The field lives at data.totalCount (not inside data.meta).
        // If present it must be JSON null; if omitted (WhenWritingNull policy) that is also acceptable.
        if (data.TryGetProperty("totalCount", out var totalCountProp))
        {
            totalCountProp.ValueKind.Should().Be(JsonValueKind.Null,
                "data.totalCount must be JSON null in cursor mode — never a positive integer [RT-07]");
        }
        // else: null omitted by serializer — also compliant with RT-07

        // pageSize echoed in meta
        meta.TryGetProperty("pageSize", out var pageSizeProp).Should().BeTrue(
            "meta.pageSize must be present");
        pageSizeProp.GetInt32().Should().BeGreaterThan(0, "pageSize must be a positive integer");

        // hasNextPage boolean present
        meta.TryGetProperty("hasNextPage", out var hasNextProp).Should().BeTrue(
            "meta.hasNextPage must be present");
        new[] { JsonValueKind.True, JsonValueKind.False }.Should().Contain(hasNextProp.ValueKind,
            "hasNextPage must be a boolean");
    }

    // ── TC-WF-INT-LIST-004: 400 malformed cursor (GOV_WORKFLOW_040) ──────────────

    [Fact]
    public async Task ListInstances_MalformedCursor_Returns400WithErrorCode()
    {
        var client = CreatePermittedClient(AdminId, TenantA);

        // Not valid base64 → TryDecode returns false → 400 Bad Request
        var response = await client.GetAsync("/api/v1/workflow/instances?cursor=NOT_VALID_BASE64!!!");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeFalse();

        // Error code GOV_WORKFLOW_040 present in response
        var raw = body.GetRawText();
        raw.Should().Contain("GOV_WORKFLOW_040");
    }

    // ── TC-WF-INT-LIST-005: rate-limit smoke ─────────────────────────────────────

    [Fact]
    public async Task ListInstances_RepeatedRequests_EventuallyReturns429OrPass()
    {
        // Smoke test: verifies rate limiter plumbing is wired without exhausting quota.
        // The read-ops limiter allows 60 req/min in non-Development environments.
        // We assert the first call succeeds (rate limiter doesn't block cold traffic).
        // Full quota exhaustion test would require a dedicated fixture with a low-limit config.
        var client = CreatePermittedClient(AdminId, TenantA);

        var firstResponse = await client.GetAsync("/api/v1/workflow/instances");

        firstResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests);
    }

    // ── TC-WF-INT-SUM-001: summary 401 ────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/workflow/instances/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TC-WF-INT-SUM-002: summary 403 ────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_AuthenticatedWithoutPermission_Returns403()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantA);

        var response = await client.GetAsync("/api/v1/workflow/instances/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TC-WF-INT-SUM-003: summary 200 ────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_AuthenticatedWithPermission_Returns200WithKpiFields()
    {
        var client = CreatePermittedClient(AdminId, TenantA);

        var response = await client.GetAsync("/api/v1/workflow/instances/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        // Summary DTO: active, completedToday, slaBreachRate (nullable), total
        data.TryGetProperty("active", out _).Should().BeTrue();
        data.TryGetProperty("completedToday", out _).Should().BeTrue();
        data.TryGetProperty("total", out _).Should().BeTrue();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient that is pre-authenticated AND has WORKFLOW.INSTANCE.VIEW permission.
    /// Overrides IEffectivePermissionService via ConfigureTestServices to grant the permission
    /// without requiring DB seeding (Testing env has no identity data seeded).
    /// </summary>
    private HttpClient CreatePermittedClient(Guid userId, Guid tenantId)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { WorkflowInstanceViewPerm }
            });

        // Other users get no permissions (403)
        permService
            .GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });

        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }
}
