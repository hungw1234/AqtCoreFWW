using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
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
/// Phase 4-B: Unclaim integration tests — 2 TDD facts (RED→GREEN).
///
/// Facts:
///   F4 Unclaim happy:    current assignee unclaims task → 200.
///                        DB: AssigneeId=null, ClaimedAt=null, Status=Open, audit Type=Unclaim.
///   F5 Unclaim idempotent: unclaim already-unclaimed task (AssigneeId=null) → 200 no-op.
///                        DB: no state change + audit ledger count UNCHANGED (no new audit row).
///
/// Seeding: minimal def+instance via API → WorkflowTask via EF.
/// </summary>
[Collection("Integration")]
public class WorkflowTasksUnclaimTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private const string TasksUrl = "/api/v1/workflow/tasks";

    // ─────────────────────────────────────────────────────────────────────
    // Seeding helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task<(Guid InstancePublicId, long InstanceInternalId, Guid TenantId)> SeedInstanceAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantIdStr = tenantId.ToString();
        using var adminClient = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantIdStr);

        var defResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Unclaim Test Workflow",
            Description = "Seeded for WorkflowTasksUnclaimTests",
            TenantId = tenantIdStr,
            CreatedBy = Guid.NewGuid(),
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
                      ActionName = "Approve", ActionLabelVi = "Phê duyệt", ActionLabelEn = "Approve",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        defResp.StatusCode.Should().Be(HttpStatusCode.OK, "definition seed must succeed");
        var defBody = await defResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionPublicId = defBody.GetProperty("data").GetProperty("id").GetGuid();

        // Phase 5b empty-pool hard-block fix.
        // P6 S2 DRY refactor (2026-05-22): extracted to WorkflowSeedHelpers.
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, definitionPublicId, PermCodes.Workflow.Task.Process);

        var instResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");
        var instBody = await instResp.Content.ReadFromJsonAsync<JsonElement>();
        var instancePublicId = instBody.GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);

        return (instancePublicId, instance.Id, tenantId);
    }

    /// <summary>
    /// Seeds a WorkflowTask with a pre-assigned AssigneeId (simulates a claimed state).
    /// Returns (taskId, assigneeId).
    /// </summary>
    private async Task<(long TaskId, Guid AssigneeId)> SeedClaimedTaskAsync(
        long instanceId, Guid tenantId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var assigneeId = Guid.NewGuid();
        var task = WorkflowTask.Create(
            workflowInstanceId: instanceId,
            title: "Unclaim Test Task",
            description: null,
            assigneeId: null,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: Guid.NewGuid());

        ctx.WorkflowTasks.Add(task);
        await ctx.SaveChangesAsync();

        // Apply Claim directly in DB to set AssigneeId + ClaimedAt via domain method.
        task.Claim(assigneeId, assigneeId);
        await ctx.SaveChangesAsync();

        return (task.Id, assigneeId);
    }

    /// <summary>
    /// Seeds an unclaimed pool WorkflowTask (AssigneeId=null). Returns taskId.
    /// </summary>
    private async Task<long> SeedUnclaimedTaskAsync(long instanceId, Guid tenantId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = WorkflowTask.Create(
            workflowInstanceId: instanceId,
            title: "Unclaim Idempotent Test Task",
            description: null,
            assigneeId: null,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: Guid.NewGuid());

        ctx.WorkflowTasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    // ─────────────────────────────────────────────────────────────────────
    // F4: Unclaim happy path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unclaim_CurrentAssignee_Returns200AndReleasesTask()
    {
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();
        var (taskId, assigneeId) = await SeedClaimedTaskAsync(instanceInternalId, tenantId);

        // The assignee unclaims their own task.
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process],
            userId: assigneeId.ToString(),
            tenantId: tenantId.ToString());

        var resp = await client.PostAsync($"{TasksUrl}/{taskId}/unclaim", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "current assignee unclaiming their own task must succeed");

        // Anti-tautology: DB assertions post-call.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = await ctx.WorkflowTasks.AsNoTracking()
            .FirstAsync(t => t.Id == taskId);

        task.AssigneeId.Should().BeNull("AssigneeId must be null after unclaim — task returns to pool");
        task.ClaimedAt.Should().BeNull("ClaimedAt must be cleared after unclaim");
        task.Status.Should().Be(WorkflowTaskStatus.Open, "Status must remain Open after unclaim");

        var auditRow = await ctx.TaskAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.WorkflowTaskId == taskId
                                   && a.Type == TaskAssignmentType.Unclaim);

        auditRow.Should().NotBeNull("TaskAssignment audit row with Type=Unclaim must be written");
        auditRow!.AssignedToId.Should().Be(assigneeId,
            "audit AssignedToId must be the unclaiming user (self-action)");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F5: Unclaim idempotent — already-unclaimed task → 200 no-op, no new audit row
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unclaim_AlreadyUnclaimedTask_Returns200WithNoAuditRow()
    {
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();
        var taskId = await SeedUnclaimedTaskAsync(instanceInternalId, tenantId);

        var userId = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process],
            userId: userId.ToString(),
            tenantId: tenantId.ToString());

        // Capture audit count BEFORE calling unclaim (should be 0 for a fresh unclaimed task).
        await using var preScope = Factory.Services.CreateAsyncScope();
        var preCtx = preScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var auditCountBefore = await preCtx.TaskAssignments.AsNoTracking()
            .CountAsync(a => a.WorkflowTaskId == taskId);

        var resp = await client.PostAsync($"{TasksUrl}/{taskId}/unclaim", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "unclaiming an already-unclaimed task must be idempotent (200, not error)");

        // Anti-tautology: audit count MUST be unchanged — no new audit row written.
        await using var postScope = Factory.Services.CreateAsyncScope();
        var postCtx = postScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var auditCountAfter = await postCtx.TaskAssignments.AsNoTracking()
            .CountAsync(a => a.WorkflowTaskId == taskId);

        auditCountAfter.Should().Be(auditCountBefore,
            "idempotent unclaim must NOT write a new audit row — count must be unchanged");

        // Confirm DB state is also unchanged (AssigneeId still null, Status still Open).
        var task = await postCtx.WorkflowTasks.AsNoTracking()
            .FirstAsync(t => t.Id == taskId);

        task.AssigneeId.Should().BeNull("task must remain unclaimed after idempotent no-op");
        task.Status.Should().Be(WorkflowTaskStatus.Open, "Status must remain Open");
        task.ClaimedAt.Should().BeNull("ClaimedAt must remain null after idempotent no-op");
    }
}
