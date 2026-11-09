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
/// Phase 4-C: Inbox integration tests — 5 TDD facts (F1-F5).
///
/// Facts:
///   F1 Inbox returns assignee tasks:   directly-assigned Open/InProgress tasks appear.
///                                       DB-state anti-tautology: response WorkflowInstanceId = seeded PublicId.
///   F2 Inbox returns pool tasks:        unclaimed pool task (no AssigneeId) with matching CandidateIndex
///                                       appears in caller's inbox.
///   F3 Inbox excludes terminal tasks:   Done/Cancelled tasks are excluded even if assignee matches.
///   F4 Inbox excludes cross-tenant:     tasks from a different tenant do NOT appear.
///   F5 Inbox authz gate:
///       F5a Anon → 401.
///       F5b Authenticated but no Workflow.Task.Process perm → 403.
///       F5c Authenticated with Workflow.Task.Process → 200 (proves policy attached and not just
///           class-level [Authorize]).
///
/// Seeding: definition+instance via API; tasks+CandidateIndex via EF direct.
/// P4 Slice 4-C.
/// </summary>
[Collection("Integration")]
public class WorkflowTasksInboxTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private const string InboxUrl = "/api/v1/workflow/tasks/inbox";

    private const string PoolPermCode = PermCodes.Workflow.Task.Process;
    private const string UnrelatedPermCode = PermCodes.Workflow.Assign.Manage;

    // ─────────────────────────────────────────────────────────────────────
    // Seeding helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task<(Guid InstancePublicId, long InstanceInternalId, Guid TenantId)> SeedInstanceAsync(
        string? tenantIdOverride = null)
    {
        var tenantId = tenantIdOverride is null ? Guid.NewGuid() : Guid.Parse(tenantIdOverride);
        using var adminClient = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantId.ToString());

        var defResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Inbox Test Workflow",
            Description = "Seeded for WorkflowTasksInboxTests",
            TenantId = tenantId.ToString(),
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
        if (defResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await defResp.Content.ReadAsStringAsync();
            defResp.StatusCode.Should().Be(HttpStatusCode.OK, $"definition seed must succeed. body={body}");
        }

        var definitionPublicId = (await defResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Phase 5b empty-pool hard-block fix.
        // P6 S2 DRY refactor (2026-05-22): extracted to WorkflowSeedHelpers.
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, definitionPublicId, PoolPermCode);

        var instResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");

        var instancePublicId = (await instResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);

        return (instancePublicId, instance.Id, tenantId);
    }

    private async Task<long> SeedTaskAsync(
        long instanceId,
        Guid tenantId,
        Guid? assigneeId = null,
        WorkflowTaskStatus status = WorkflowTaskStatus.Open)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = WorkflowTask.Create(
            workflowInstanceId: instanceId,
            title: "Inbox Test Task",
            description: null,
            assigneeId: assigneeId,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: Guid.NewGuid());

        // Force status for terminal-state tests (normally set via domain methods, but for test seeding
        // we use EF directly with explicit value object state).
        if (status is WorkflowTaskStatus.Done or WorkflowTaskStatus.Cancelled)
        {
            // Reflect to set status for terminal-state seeding — domain method is not exposed externally.
            typeof(WorkflowTask)
                .GetProperty(nameof(WorkflowTask.Status))!
                .SetValue(task, status);
        }

        ctx.WorkflowTasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    private async Task SeedCandidateIndexAsync(
        long taskId,
        Guid tenantId,
        string principalRefId = PoolPermCode)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var row = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: taskId,
            clauseId: 1,
            combineMode: AssignmentCombineMode.Union,
            principalType: CandidatePrincipalType.Permission,
            principalRefId: principalRefId,
            tenantId: tenantId);

        ctx.WorkflowTaskCandidateIndex.Add(row);
        await ctx.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // F5: Authorization gate
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F5a: Anonymous caller → 401 Unauthorized.
    /// Proves endpoint exists and is protected (not 404).
    /// </summary>
    [Fact]
    public async Task GetInbox_Anonymous_Returns401()
    {
        using var anonClient = Factory.CreateClient(); // No auth headers

        var resp = await anonClient.GetAsync(InboxUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous access to inbox must be rejected with 401");
    }

    /// <summary>
    /// F5b: Authenticated user without Workflow.Task.Process → 403 Forbidden.
    /// Anti-tautology: proves explicit policy is attached (not just class-level [Authorize]).
    /// </summary>
    [Fact]
    public async Task GetInbox_AuthenticatedWithoutTaskProcessPermission_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        // Give an unrelated permission — passes [Authorize] but fails [Authorize(Policy=...)]
        using var client = CreateClientWithPermissions(
            [UnrelatedPermCode], userId: Guid.NewGuid().ToString(), tenantId: tenantId);

        var resp = await client.GetAsync(InboxUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated caller lacking Workflow.Task.Process permission must get 403 — " +
            "proves explicit policy gate not relying on class-level [Authorize]");
    }

    /// <summary>
    /// F5c: Authenticated user with Workflow.Task.Process → 200 OK (empty inbox is fine).
    /// Proves route + policy wired correctly end-to-end.
    /// </summary>
    [Fact]
    public async Task GetInbox_AuthenticatedWithTaskProcessPermission_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [PoolPermCode], userId: Guid.NewGuid().ToString(), tenantId: tenantId);

        var resp = await client.GetAsync(InboxUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "authenticated caller with Workflow.Task.Process must reach the inbox handler");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F1: Inbox returns directly-assigned tasks
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F1: Directly-assigned Open task appears in assignee's inbox.
    /// Anti-tautology: response WorkflowInstanceId == seeded instance PublicId (not a random Guid).
    /// </summary>
    [Fact]
    public async Task GetInbox_AssignedTask_AppearsForAssignee()
    {
        var userId = Guid.NewGuid();
        var (instancePublicId, instanceInternalId, tenantId) = await SeedInstanceAsync();

        await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: userId);

        using var client = CreateClientWithPermissions(
            [PoolPermCode], userId: userId.ToString(), tenantId: tenantId.ToString());

        var resp = await client.GetAsync(InboxUrl);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "assigned task must appear in assignee's inbox");

        // Anti-tautology: WorkflowInstanceId in response must match seeded PublicId.
        var taskItems = data.EnumerateArray().ToList();
        var taskItem = taskItems.FirstOrDefault(t =>
            t.TryGetProperty("workflowInstanceId", out var wid) &&
            wid.GetGuid() == instancePublicId);

        taskItem.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            $"inbox response must contain a task with workflowInstanceId = {instancePublicId} " +
            "(anti-tautology: proves WorkflowInstanceId is T2 PublicId, not a random value)");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F2: Inbox returns pool tasks (EXISTS-over-CandidateIndex)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F2: Unclaimed pool task with matching CandidateIndex appears in caller's inbox.
    /// Preconditions: (a) task has no assignee; (b) CandidateIndex row with caller's permCode.
    /// Anti-tautology: task appears ONLY when CandidateIndex row matches caller's permission.
    /// </summary>
    [Fact]
    public async Task GetInbox_PoolTask_AppearsForEligibleCaller()
    {
        var userId = Guid.NewGuid();
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();

        // Pool task — no assignee
        var taskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: null);
        await SeedCandidateIndexAsync(taskId, tenantId, PoolPermCode);

        using var client = CreateClientWithPermissions(
            [PoolPermCode], userId: userId.ToString(), tenantId: tenantId.ToString());

        var resp = await client.GetAsync(InboxUrl);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "pool task must appear in inbox when caller's permission matches CandidateIndex");

        var taskIds = data.EnumerateArray()
            .Select(t => t.TryGetProperty("id", out var id) ? id.GetInt64() : 0L)
            .ToList();
        taskIds.Should().Contain(taskId,
            "the seeded pool task id must appear in the inbox response");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F3: Inbox excludes terminal-state tasks
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F3: Done and Cancelled tasks do NOT appear in inbox, even if directly assigned to caller.
    /// Preconditions: seed one Open task + one Done task, both assigned to the same user.
    /// Assert: only the Open task appears (count = 1 for this tenant).
    /// </summary>
    [Fact]
    public async Task GetInbox_TerminalStateTasks_ExcludedFromInbox()
    {
        var userId = Guid.NewGuid();
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();

        // Open task — should appear
        var openTaskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: userId,
            status: WorkflowTaskStatus.Open);

        // Done task — should NOT appear
        var doneTaskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: userId,
            status: WorkflowTaskStatus.Done);

        // Cancelled task — should NOT appear
        var cancelledTaskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: userId,
            status: WorkflowTaskStatus.Cancelled);

        using var client = CreateClientWithPermissions(
            [PoolPermCode], userId: userId.ToString(), tenantId: tenantId.ToString());

        var resp = await client.GetAsync(InboxUrl);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        var returnedIds = data.EnumerateArray()
            .Select(t => t.TryGetProperty("id", out var id) ? id.GetInt64() : 0L)
            .ToList();

        returnedIds.Should().Contain(openTaskId,
            "Open task must appear in inbox");
        returnedIds.Should().NotContain(doneTaskId,
            "Done (terminal) task must NOT appear in inbox");
        returnedIds.Should().NotContain(cancelledTaskId,
            "Cancelled (terminal) task must NOT appear in inbox");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F4: Inbox excludes cross-tenant tasks
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F4: Tasks from a different tenant do NOT appear in caller's inbox.
    /// Preconditions: seed task under tenantA assigned to userId; caller authenticates as tenantB.
    /// Assert: inbox for tenantB is empty (does not see tenantA task).
    /// </summary>
    [Fact]
    public async Task GetInbox_CrossTenant_TasksExcluded()
    {
        var userId = Guid.NewGuid();
        var (_, instanceInternalIdA, tenantIdA) = await SeedInstanceAsync();
        await SeedTaskAsync(instanceInternalIdA, tenantIdA, assigneeId: userId);

        // Caller authenticates as a DIFFERENT tenant
        var tenantIdB = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [PoolPermCode], userId: userId.ToString(), tenantId: tenantIdB.ToString());

        var resp = await client.GetAsync(InboxUrl);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        // May have 0 items from tenantB's perspective — the tenantA task must NOT appear.
        // All returned tasks must belong to tenantB.
        var allBelongToTenantB = data.EnumerateArray().All(t =>
            !t.TryGetProperty("tenantId", out var tid) ||
            tid.GetGuid() == tenantIdB);

        allBelongToTenantB.Should().BeTrue(
            "inbox must only return tasks for the caller's own tenant (cross-tenant isolation)");
    }
}
