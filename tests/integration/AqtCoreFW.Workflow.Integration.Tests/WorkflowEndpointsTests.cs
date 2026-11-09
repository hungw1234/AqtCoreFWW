using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// Integration tests for Workflow REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// Covers: definitions list, assignments list, auth enforcement, cross-tenant isolation.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public WorkflowEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    // --- GET /api/v1/workflow/definitions ---

    [Fact]
    public async Task ListDefinitions_Authenticated_Returns200()
    {
        // TenantId resolved from TestAuthHandler X-Test-TenantId header — no query param needed.
        // SystemAdmin role triggers TestSystemAdminPermissionBypassHandler which bypasses
        // PermissionRequirement DB lookup for test users with no DB-backed permission rows.
        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "SystemAdmin");

        var response = await client.GetAsync("/api/v1/workflow/definitions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListDefinitions_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/v1/workflow/definitions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListDefinitions_ResponseWrappedInApiEnvelope()
    {
        // SystemAdmin bypasses PermissionRequirement — required because WORKFLOW.DEF.VIEW policy
        // triggers a DB permission lookup; test users have no DB rows.
        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "SystemAdmin");

        var response = await client.GetAsync("/api/v1/workflow/definitions");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // ApiResponse<T> envelope always has "success" property
        body.TryGetProperty("success", out _).Should().BeTrue();
    }

    // --- GET /api/v1/workflow/assignments ---

    [Fact]
    public async Task ListAssignments_Authenticated_Returns200()
    {
        // SystemAdmin bypasses Assign.Manage policy via TestSystemAdminPermissionBypassHandler.
        // GET /assignments was elevated from naked [Authorize] to Policy=Workflow.Assign.Manage
        // by the 4-A-A INVARIANT closure (WorkflowAssignmentsController.cs:22-23); GovOfficer
        // role no longer carries that perm post-RBAC v2.133p6 + 4-A-A. Mirrors ListDefinitions
        // precedent above (lines 33-35).
        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "SystemAdmin");

        var response = await client.GetAsync("/api/v1/workflow/assignments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListAssignments_CrossTenant_Isolation()
    {
        // Arrange: list assignments as TenantA and TenantB — each must see only their own data.
        // Assignment rules are tenant-scoped (GetAssignmentRulesQuery filters by ResolveTenantId()).
        // SystemAdmin bypasses the Assign.Manage policy gate (see ListAssignments_Authenticated_Returns200)
        // but NOT tenant scoping — ResolveTenantId() reads the JWT tenant_id claim independently of role,
        // so each client remains scoped to its declared tenant.
        var clientA = _app.CreateAuthenticatedClient(UserId, TenantA, "SystemAdmin");
        var clientB = _app.CreateAuthenticatedClient(UserId, TenantB, "SystemAdmin");

        var responseA = await clientA.GetAsync("/api/v1/workflow/assignments");
        var responseB = await clientB.GetAsync("/api/v1/workflow/assignments");

        // Both return 200 — tenant isolation is enforced at query level, not HTTP layer
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);

        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();

        // Verify both responses have the success envelope
        bodyA.TryGetProperty("success", out var successA).Should().BeTrue();
        bodyB.TryGetProperty("success", out var successB).Should().BeTrue();
        successA.GetBoolean().Should().BeTrue();
        successB.GetBoolean().Should().BeTrue();

        // Both return independent result sets — no cross-tenant leakage.
        // Serialize to compare: different tenant IDs must yield logically independent data sets.
        var rawA = bodyA.GetRawText();
        var rawB = bodyB.GetRawText();

        // Serialized JSON must not contain the OTHER tenant's ID
        rawA.Should().NotContain(TenantB.ToString());
        rawB.Should().NotContain(TenantA.ToString());
    }
}
