using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Tests.Integration.Workflow;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.BusinessProcess;

/// <summary>
/// Phase 5b first-task-on-instance-creation coverage for the BusinessProcess path (I3 + I4).
///
/// I1/I2 in <see cref="WorkflowInstancesCreateFirstTaskTests"/> exercise the same behavior through
/// POST /api/v1/workflow/instances. The BP path reaches the identical instance-creation core
/// (<c>InProcessWorkflowModuleClient.CreateInstanceByInternalIdAsync → CreateInstanceAsync</c>) but
/// through a different controller and the cross-module unit-of-work.
///
/// I3 (happy path): start a BP-Run whose pinned definition has a non-empty pool at the initial state
///   → 200; exactly one Open first-task is staged atomically with the instance.
///
/// I4 (no-handler blocked at activation): the BP path blocks the empty-pool / no-handler case EARLIER
///   than the workflow-instances path does. BP activation runs design-time graph validation (R1-R4 via
///   IWorkflowModuleClient.ValidateDefinitionGraphAsync); R2 requires every human-actionable outgoing
///   transition of a non-final state to carry a handler assignment. A definition whose initial-state
///   transition has no assignment therefore cannot be activated (422 GOV_WFL_019), so a run can never
///   start against it. This is the BP-path analogue of I2's empty-pool block, enforced at the activation
///   gate rather than at run-start (the gate post-dates the Phase 5b spec that asked for a run-start test).
///
/// No intake form is pinned — that keeps these tests focused on the workflow-creation gate, mirroring the
/// form-free I1/I2. Cross-module DTC/atomicity with an intake form is covered separately by
/// <see cref="StartBusinessProcessRunTransactionTests"/>.
///
/// Post-action assertions use <c>.IgnoreQueryFilters().AsNoTracking()</c> per the 4-C-A defense
/// against EF model-cache contamination in the shared <c>ApiFactory</c>.
/// </summary>
[Collection("Integration")]
public class StartBusinessProcessRunFirstTaskTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BpBaseUrl = "/api/v1/business-processes";
    private const string WorkflowDefinitionsUrl = "/api/v1/workflow/definitions";
    private const string PoolPermCode = PermCodes.Workflow.Task.Process;

    // ── I3 — happy path: BP-Run creates instance + exactly one Open first-task atomically ──────────

    [Fact]
    public async Task StartRun_NonFinalInitialWithValidPool_Creates_Instance_And_FirstTask_Atomically()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();

        using var actor = CreateAuthenticatedClient(
            roles: ["SystemAdmin"], tenantId: tenantId, userId: actorId);

        var definitionPublicId = await CreateWorkflowDefinitionAsync(actor, tenantId, Guid.Parse(actorId));
        // Non-empty pool at the initial state → definition passes the activation graph gate (R2)
        // and BP-Run instance creation succeeds.
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, definitionPublicId, PoolPermCode, actionName: "Submit");
        var bpId = await CreateBpAsync(actor, tenantId, "BP-FT-I3", definitionPublicId);

        var activateResp = await actor.PostAsync($"{BpBaseUrl}/{bpId}/activate", null);
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a definition with a valid initial-state pool must pass the activation graph gate");

        var entityId = Guid.NewGuid();
        var startResp = await actor.PostAsJsonAsync($"{BpBaseUrl}/{bpId}/runs", new
        {
            EntityType = "Generic",
            EntityId = entityId,
            ConsentGiven = false,
        });

        var startRaw = await startResp.Content.ReadAsStringAsync();
        startResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"valid pool at initial state must allow BP-Run creation. Body: {startRaw}");
        var instancePublicId = JsonSerializer.Deserialize<JsonElement>(startRaw)
            .GetProperty("data").GetProperty("workflowInstanceId").GetGuid();

        // Post-action ground-truth assertions via .IgnoreQueryFilters().AsNoTracking() (4-C-A defense).
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances
            .IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(i => i.PublicId == instancePublicId);
        instance.Should().NotBeNull("BP-Run must persist the workflow instance on the happy path");

        var tasksForInstance = await ctx.WorkflowTasks
            .IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instance!.Id)
            .ToListAsync();
        tasksForInstance.Should().HaveCount(1,
            "Phase 5b must stage exactly one first-task atomically with the BP-created instance");

        var task = tasksForInstance[0];
        task.Status.Should().Be(WorkflowTaskStatus.Open, "newly-created first task is Open");
        task.CandidateRefsJson.Should().NotBeNull(
            "ByPermission pool snapshot must be serialized into CandidateRefsJson (DCAND-b JSON-only)");
        task.AssigneeId.Should().BeNull("pool path has no direct assignee — caller resolves later");
        task.TenantId.Should().Be(instance!.TenantId, "task tenant must match instance tenant");
    }

    // ── I4 — no-handler blocked at the activation graph gate (R2): 422, BP stays inactive ──────────

    [Fact]
    public async Task Activate_NonFinalInitialWithNoAssignment_Returns422_GraphValidationGate()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();

        using var actor = CreateAuthenticatedClient(
            roles: ["SystemAdmin"], tenantId: tenantId, userId: actorId);

        // No assignment seeded → the initial-state transition has no handler → R2 graph violation.
        var definitionPublicId = await CreateWorkflowDefinitionAsync(actor, tenantId, Guid.Parse(actorId));
        var bpId = await CreateBpAsync(actor, tenantId, "BP-FT-I4", definitionPublicId);

        var activateResp = await actor.PostAsync($"{BpBaseUrl}/{bpId}/activate", null);

        activateResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a definition whose non-final initial state has a transition with no handler assignment "
            + "must fail the activation graph gate (R2) before any run can start");

        var body = await activateResp.Content.ReadAsStringAsync();
        body.Should().Contain(WorkflowErrorCodes.GraphValidationFailed,
            "activation graph failure surfaces GOV_WFL_019");
        body.Should().Contain("R2",
            "the firing rule is R2 (missing handler assignment), not R1/R3/R4");

        // Rollback proof: the graph gate runs BEFORE bp.Activate(), so IsActive must remain false.
        await using var scope = Factory.Services.CreateAsyncScope();
        var bpCtx = scope.ServiceProvider.GetRequiredService<BusinessProcessDbContext>();
        var bp = await bpCtx.BusinessProcessDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(b => b.PublicId == bpId);
        bp.Should().NotBeNull("the BP row must still exist after a rejected activation");
        bp!.IsActive.Should().BeFalse("a graph-rejected BP must not be flipped active");
    }

    // ----- HELPERS -----

    /// <summary>
    /// Seeds a Draft (initial, non-final) → Approved (final) definition with a single "Submit"
    /// outgoing transition (no handler assignment). Returns the definition Guid PublicId. Whether the
    /// Submit transition carries a pool is controlled by the caller via <see cref="WorkflowSeedHelpers"/>.
    /// </summary>
    private async Task<Guid> CreateWorkflowDefinitionAsync(HttpClient client, string tenantId, Guid actorId)
    {
        var defResp = await client.PostAsJsonAsync(WorkflowDefinitionsUrl, new
        {
            Name = $"bp-firsttask-{Guid.NewGuid():N}",
            Description = "Phase 5b I3/I4 BP-path seed",
            TenantId = tenantId,
            CreatedBy = actorId,
            EntityType = "Generic",
            States = new[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "Approved", DisplayNameVi = "Đã duyệt", DisplayNameEn = "Approved",
                      IsInitial = false, IsFinal = true, Color = "#52c41a", SortOrder = 2 },
            },
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "Approved",
                      ActionName = "Submit", ActionLabelVi = "Nộp", ActionLabelEn = "Submit",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        var defRaw = await defResp.Content.ReadAsStringAsync();
        defResp.StatusCode.Should().Be(HttpStatusCode.OK, $"definition seed must succeed. Body: {defRaw}");
        return JsonSerializer.Deserialize<JsonElement>(defRaw)
            .GetProperty("data").GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Creates a BusinessProcess pinning <paramref name="workflowDefinitionPublicId"/> (no intake form)
    /// and returns its Guid PublicId. Does NOT activate — callers assert the activation outcome themselves.
    /// Code uses an uppercase-dash suffix to satisfy the create validator's <c>^[A-Z0-9_-]+$</c> rule.
    /// </summary>
    private async Task<Guid> CreateBpAsync(
        HttpClient client, string tenantId, string codePrefix, Guid workflowDefinitionPublicId)
    {
        var code = $"{codePrefix}-{Guid.NewGuid():N}".ToUpperInvariant();
        var createResp = await client.PostAsJsonAsync(BpBaseUrl, new
        {
            Code = code,
            Name = $"{codePrefix} first-task test",
            Description = "Phase 5b I3/I4 BP-path first-task coverage",
            TenantId = tenantId,
            WorkflowDefinitionPublicId = workflowDefinitionPublicId,
        });
        var createRaw = await createResp.Content.ReadAsStringAsync();
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, $"BP creation must succeed. Body: {createRaw}");
        return JsonSerializer.Deserialize<JsonElement>(createRaw).GetProperty("data").GetGuid();
    }
}
