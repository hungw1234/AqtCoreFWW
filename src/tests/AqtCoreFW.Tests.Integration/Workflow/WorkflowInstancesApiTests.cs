using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Integration tests for WorkflowInstancesController — /api/v1/workflow/instances
///
/// Routes (Phase-04-A follow-up 2026-05-22 4-A-A invariant closure):
///   POST /api/v1/workflow/instances                                    → Create [WORKFLOW.INSTANCE.MANAGE]
///   GET  /api/v1/workflow/instances                                    → List [WORKFLOW.INSTANCE.VIEW]
///   GET  /api/v1/workflow/instances/summary                            → Summary [WORKFLOW.INSTANCE.VIEW]
///   GET  /api/v1/workflow/instances/{id:guid}                          → GetById [BUSINESSPROCESS.BP.VIEW]
///   GET  /api/v1/workflow/instances/{id:guid}/available-transitions    → Available transitions [BUSINESSPROCESS.BP.VIEW]
///   GET  /api/v1/workflow/instances/{id:guid}/audit-export             → Export audit [WORKFLOW.INSTANCE.VIEW]
///   GET  /api/v1/workflow/instances/{id:guid}/branch-status            → Branch status [WORKFLOW.INSTANCE.VIEW]
///
/// Seed pattern: create a WorkflowDefinition via POST, stage a ByPermission([Task.Process])
/// assignment on the outgoing transition (Phase 5b requirement), then start a WorkflowInstance.
/// Both responses expose Guid PublicId in data.id (T2 pattern).
///
/// Phase-4b B4c: WorkflowInstance is T2 — Id = Guid PublicId, internal Id = bigint PK.
///   GetById/AvailableTransitions route param = Guid PublicId.
///   Handlers resolve PublicId → internal long PK for SQL JOINs.
/// </summary>
[Collection("Integration")]
public class WorkflowInstancesApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";

    // Helper: seed a WorkflowDefinition and return its Guid PublicId.
    // Uses a 2-state definition with 1 transition so GetAvailableTransitions has a result.
    // EntityType "Generic" avoids entity-type-specific validation in handler.
    private async Task<(Guid DefinitionId, HttpClient AdminClient, string TenantId)> SeedDefinitionAsync()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        var createResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Instance Test Workflow",
            Description = "Definition seeded for WorkflowInstancesApiTests",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "Nháp",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = false,
                    Color = "#2196F3",
                    SortOrder = 1,
                },
                new
                {
                    Name = "Approved",
                    DisplayNameVi = "Đã duyệt",
                    DisplayNameEn = "Approved",
                    IsInitial = false,
                    IsFinal = true,
                    Color = "#607D8B",
                    SortOrder = 2,
                },
            },
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "Approved",
                    ActionName = "Approve",
                    ActionLabelVi = "Phê duyệt",
                    ActionLabelEn = "Approve",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                },
            },
        });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "definition seed must succeed before creating instances");

        var body = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionId = body.GetProperty("data").GetProperty("id").GetGuid();
        definitionId.Should().NotBe(Guid.Empty, "created definition must have a non-empty Guid PublicId");

        // Phase 5b empty-pool hard-block fix: stage a ByPermission([Workflow.Task.Process]) assignment
        // on Approve (Draft's outgoing transition) so initial-state pool resolves non-empty and the
        // subsequent POST /instances does not 422 with GOV_WFL_014.
        // P6 S2 DRY refactor (2026-05-22): extracted to WorkflowSeedHelpers.
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, definitionId, PermCodes.Workflow.Task.Process);

        return (definitionId, adminClient, tenantId);
    }

    // Helper: seed a WorkflowInstance and return its Guid PublicId.
    private async Task<Guid> SeedInstanceAsync(Guid definitionId, HttpClient adminClient, string tenantId)
    {
        var entityId = Guid.NewGuid();
        var createResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = entityId,
            DefinitionId = definitionId,
        });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "instance seed must succeed; check definition exists and handler resolves PublicId correctly");

        var body = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var instanceId = body.GetProperty("data").GetProperty("id").GetGuid();
        instanceId.Should().NotBe(Guid.Empty, "created instance must have a non-empty Guid PublicId");

        return instanceId;
    }

    // -----------------------------------------------------------------
    // GET BY ID
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetWorkflowInstanceById_ForExistingInstance_Returns200WithGuidId()
    {
        var (definitionId, adminClient, tenantId) = await SeedDefinitionAsync();
        var instanceId = await SeedInstanceAsync(definitionId, adminClient, tenantId);

        var response = await adminClient.GetAsync($"{InstancesUrl}/{instanceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue("response must be ApiResponse envelope");

        // T2 pattern: data.id = Guid PublicId (not long internal PK)
        data.GetProperty("id").GetGuid().Should().Be(instanceId,
            "GetById must resolve PublicId→internal PK and surface Guid AK back to caller");

        // Verify key structural fields
        data.TryGetProperty("entityType", out _).Should().BeTrue();
        data.TryGetProperty("currentStateId", out _).Should().BeTrue();
        data.TryGetProperty("history", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetWorkflowInstanceById_ForNonExistentId_Returns404()
    {
        var nonExistentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantId);

        var response = await client.GetAsync($"{InstancesUrl}/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // LIST
    // -----------------------------------------------------------------

    [Fact]
    public async Task ListWorkflowInstances_AsAuthenticatedUser_Returns200WithEnvelope()
    {
        var (definitionId, adminClient, tenantId) = await SeedDefinitionAsync();
        await SeedInstanceAsync(definitionId, adminClient, tenantId);

        // List requires WORKFLOW.INSTANCE.VIEW permission — use SystemAdmin client which holds all perms
        var response = await adminClient.GetAsync(InstancesUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue("response must be ApiResponse envelope");

        // PagedResult shape: data.items (array) + data.meta
        data.TryGetProperty("items", out var items).Should().BeTrue("paged result must have items array");
        data.TryGetProperty("meta", out _).Should().BeTrue("paged result must have meta object");

        // At least the seeded instance is visible (tenant-scoped query)
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "seeded instance must appear in tenant-scoped list");

        // Each item must expose Guid Id (PublicId AK, not long internal PK)
        var firstItem = items.EnumerateArray().First();
        firstItem.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    // -----------------------------------------------------------------
    // AVAILABLE TRANSITIONS
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetAvailableTransitions_ForRunningInstance_Returns200WithTransitions()
    {
        var (definitionId, adminClient, tenantId) = await SeedDefinitionAsync();
        var instanceId = await SeedInstanceAsync(definitionId, adminClient, tenantId);

        // Instance starts at "Draft" state which has 1 outgoing transition ("Approve")
        var response = await adminClient.GetAsync($"{InstancesUrl}/{instanceId}/available-transitions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue("response must be ApiResponse envelope");

        // data is array of available transitions from the current (initial) state
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "initial 'Draft' state has 1 outgoing transition 'Approve' with no permission guard");
    }

    [Fact]
    public async Task GetAvailableTransitions_ForNonExistentInstance_Returns404()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantId);

        var response = await client.GetAsync($"{InstancesUrl}/{Guid.NewGuid()}/available-transitions");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // EXPORT AUDIT
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExportInstanceAudit_AsJson_Returns200WithJsonContent()
    {
        var (definitionId, adminClient, tenantId) = await SeedDefinitionAsync();
        var instanceId = await SeedInstanceAsync(definitionId, adminClient, tenantId);

        // ExportAudit returns File(...) directly — not wrapped in ApiResponse envelope.
        // Default format = JSON (AuditExportFormat.Json)
        var response = await adminClient.GetAsync($"{InstancesUrl}/{instanceId}/audit-export?format=json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Content-Type must be application/json (not ApiResponse envelope)
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "ExportAudit returns File(...) with application/json content type, not ApiResponse envelope");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty("audit export body must be non-empty even for a new instance");
    }

    // -----------------------------------------------------------------
    // SUMMARY
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetWorkflowInstancesSummary_AsAuthenticatedUser_Returns200WithCounts()
    {
        var (definitionId, adminClient, tenantId) = await SeedDefinitionAsync();
        await SeedInstanceAsync(definitionId, adminClient, tenantId);

        // Summary requires WORKFLOW.INSTANCE.VIEW — SystemAdmin client satisfies this
        var response = await adminClient.GetAsync($"{InstancesUrl}/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue("response must be ApiResponse envelope");

        // WorkflowInstanceSummaryDto shape: active, completedToday, slaBreachRate (nullable), total
        data.TryGetProperty("active", out _).Should().BeTrue();
        data.TryGetProperty("completedToday", out _).Should().BeTrue();
        data.TryGetProperty("total", out var totalEl).Should().BeTrue();

        // At least 1 seeded instance visible in tenant scope
        totalEl.GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // -----------------------------------------------------------------
    // ANTI-TAUTOLOGY: Workflow.Instance.Manage NECESSARY for POST /instances
    // -----------------------------------------------------------------

    /// <summary>
    /// P6 S3 anti-tautology fact (PHR-V2 closure, 260522-E pattern).
    ///
    /// Phase 4-A-A locked CREATE behind `Workflow.Instance.Manage`. Admin-positive tests prove
    /// the gate is SUFFICIENT (admin with full perm-set → 200); anon-401 tests prove SOMETHING
    /// more than anonymous is needed. Neither path proves Workflow.Instance.Manage is
    /// NECESSARY — a principal granted every OTHER PermCode could still pass if the gate were
    /// misconfigured.
    ///
    /// This fact locks the contract: a principal with ALL PermCodes EXCEPT
    /// `WORKFLOW.INSTANCE.MANAGE` MUST be rejected with 403. If a future seeder regression or
    /// policy-rename ever weakens the gate (e.g. fallback to View), this fact fails loudly.
    /// </summary>
    [Fact]
    public async Task CreateInstance_PrincipalWithAllPermsExceptInstanceManage_Returns403()
    {
        var (definitionId, adminClient, tenantId) = await SeedDefinitionAsync();

        // Principal granted EVERY PermCode except Workflow.Instance.Manage.
        var permsExceptInstanceManage = PermCodes.All
            .Where(p => !string.Equals(p, PermCodes.Workflow.Instance.Manage, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        permsExceptInstanceManage.Should().NotContain(PermCodes.Workflow.Instance.Manage,
            "anti-tautology guard — set must exclude the target PermCode");
        permsExceptInstanceManage.Length.Should().BeGreaterThan(0,
            "PermCodes.All must contain at least one non-target code");

        using var fineClient = CreateClientWithPermissions(
            permsExceptInstanceManage,
            tenantId: tenantId);

        var response = await fineClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Workflow.Instance.Manage is NECESSARY — every other PermCode cannot substitute. " +
            "If this regresses to 200, the policy gate has weakened (likely fallback to View " +
            "or a seeder change widening grants). Lock contract per 4-A-A invariant.");
    }
}
