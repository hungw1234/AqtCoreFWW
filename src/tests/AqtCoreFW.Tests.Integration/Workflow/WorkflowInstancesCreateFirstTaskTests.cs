using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 5b — first-task-on-instance-creation HTTP integration tests (I1 + I2).
///
/// I1 (happy path): POST /api/v1/workflow/instances with a definition whose initial state has a
///   ByPermission outgoing assignment → 200; exactly 1 instance + 1 task persisted atomically.
/// I2 (empty pool blocks): POST same endpoint with a definition whose initial state has NO outgoing
///   assignments → 422 GOV_WFL_014; zero instance + zero task rows persisted (atomicity invariant).
///
/// Post-action assertions use <c>.IgnoreQueryFilters().AsNoTracking()</c> per 4-C-A defense against
/// EF model-cache contamination in <c>WebAppFactory</c>.
///
/// BP-path coverage (I3/I4) lives in <see cref="AqtCoreFW.Tests.Integration.BusinessProcess.StartBusinessProcessRunFirstTaskTests"/>.
/// I3 mirrors the I1 happy path through POST /api/v1/business-processes/{id}/runs. The BP path blocks the
/// no-handler case earlier than this one: BP activation runs the R1-R4 graph gate, so a definition with an
/// unassigned initial-state transition is rejected at activation (I4) rather than at run-start (I2 here).
/// </summary>
[Collection("Integration")]
public class WorkflowInstancesCreateFirstTaskTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private const string PoolPermCode = PermCodes.Workflow.Task.Process;

    /// <summary>
    /// Seeds a Draft (initial, non-final) → Approved (final) workflow. Returns the definition PublicId.
    /// <paramref name="addInitialAssignment"/> controls whether the Submit transition (outgoing of
    /// Draft) carries a ByPermission([Task.Process]) clause — true = creation succeeds, false =
    /// resolver returns empty pool → 5b hard-blocks.
    /// </summary>
    private async Task<Guid> SeedDefinitionAsync(string tenantId, string actorId, bool addInitialAssignment)
    {
        using var adminClient = CreateAuthenticatedClient(
            roles: ["SystemAdmin"], tenantId: tenantId, userId: actorId);

        var defResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = $"5b-test-{Guid.NewGuid():N}",
            Description = "Phase 5b I1/I2 seed",
            TenantId = tenantId,
            CreatedBy = Guid.Parse(actorId),
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
        defResp.StatusCode.Should().Be(HttpStatusCode.OK, "definition seed must succeed");
        var definitionPublicId = (await defResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        if (addInitialAssignment)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await ctx.WorkflowDefinitions.AsNoTracking()
                .FirstAsync(d => d.PublicId == definitionPublicId);
            var submitTr = await ctx.Set<WorkflowTransition>().AsNoTracking()
                .FirstAsync(t => t.DefinitionId == def.Id && t.ActionName == "Submit");

            var assignment = WorkflowTransitionAssignment.Create(
                workflowTransitionId: submitTr.Id,
                combineMode: AssignmentCombineMode.Union);
            assignment.AddClause(AssignmentClauseMode.ByPermission, $"""["{PoolPermCode}"]""");
            ctx.Set<WorkflowTransitionAssignment>().Add(assignment);
            await ctx.SaveChangesAsync();
        }
        // else: no assignment on Submit → initial-state pool resolves Empty → 5b hard-blocks.

        return definitionPublicId;
    }

    // ── I1 — happy path: instance + first task created atomically ────────────────

    [Fact]
    public async Task PostInstance_NonFinalInitialWithValidPool_Creates_Instance_And_FirstTask_Atomically()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();
        var definitionPublicId = await SeedDefinitionAsync(tenantId, actorId, addInitialAssignment: true);

        using var actor = CreateAuthenticatedClient(
            roles: ["SystemAdmin"], tenantId: tenantId, userId: actorId);

        var resp = await actor.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "valid pool at initial state must allow creation");
        var instancePublicId = (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Post-action ground-truth assertions via .IgnoreQueryFilters().AsNoTracking() (4-C-A defense).
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances
            .IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(i => i.PublicId == instancePublicId);
        instance.Should().NotBeNull("instance row must be persisted on the happy path");

        var tasksForInstance = await ctx.WorkflowTasks
            .IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instance!.Id)
            .ToListAsync();
        tasksForInstance.Should().HaveCount(1,
            "Phase 5b must stage exactly one first-task atomically with the instance");

        var task = tasksForInstance[0];
        task.Status.Should().Be(WorkflowTaskStatus.Open,
            "newly-created first task is Open");
        task.CandidateRefsJson.Should().NotBeNull(
            "ByPermission pool snapshot must be serialized into CandidateRefsJson (DCAND-b JSON-only)");
        task.AssigneeId.Should().BeNull(
            "pool path has no direct assignee — caller resolves later");
        task.TenantId.Should().Be(instance!.TenantId,
            "task tenant must match instance tenant");
    }

    // ── I2 — empty pool blocks: 422 + atomic rollback ──────────────────────────────

    [Fact]
    public async Task PostInstance_NonFinalInitialWithEmptyPool_Returns422_NoOrphanRows()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();
        var definitionPublicId = await SeedDefinitionAsync(tenantId, actorId, addInitialAssignment: false);
        var entityId = Guid.NewGuid();

        using var actor = CreateAuthenticatedClient(
            roles: ["SystemAdmin"], tenantId: tenantId, userId: actorId);

        var resp = await actor.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = entityId,
            DefinitionId = definitionPublicId,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "Phase 5b hard-blocks empty-pool initial state with GOV_WFL_014");

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(WorkflowErrorCodes.NoHandlerResolved,
            "body code must surface GOV_WFL_014 (4-B-A dual-metadata invariant)");

        // Atomicity ground-truth: NO instance and NO task rows for the rejected creation.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var orphanInstances = await ctx.WorkflowInstances
            .IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.EntityType == "Generic" && i.EntityId == entityId)
            .ToListAsync();
        orphanInstances.Should().BeEmpty(
            "atomicity invariant: 5b must reject before staging any instance row");

        var orphanTasks = await ctx.WorkflowTasks
            .IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == Guid.Parse(tenantId))
            .ToListAsync();
        orphanTasks.Should().BeEmpty(
            "atomicity invariant: no first-task can be staged for a rejected creation");
    }
}
