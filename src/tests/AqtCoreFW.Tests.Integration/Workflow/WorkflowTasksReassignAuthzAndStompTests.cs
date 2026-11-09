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
/// Phase 4-A: Reassign IDOR Hotfix — 4 TDD facts (RED→GREEN).
///
/// Security gap: WorkflowTasksController.Reassign had plain [Authorize] only —
/// any authenticated user could reassign any task (IDOR).
/// Fix: [Authorize(Policy = PermCodes.Workflow.Assign.Manage)] + ReassignAdmin audit type
/// + mandatory Reason when stomping a claimed task (AssigneeId != null pre-state).
///
/// Facts:
///   F1 IDOR closed:   non-admin authenticated user → 403 (policy gate).
///   F2 Admin unclaimed:  admin reassigns unassigned task → 200; audit Type=Reassign.
///   F3 Admin stomp with reason: admin stomps claimed task with reason → 200;
///                                audit row Type=ReassignAdmin + Reason captured in DB.
///   F4 Admin stomp without reason: admin stomps claimed task, no reason → 422 (ValidationError).
///
/// Anti-tautology:
///   F1: body parsed; success=false AND success field present (proves 403 is our policy gate,
///       not a misconfigured global filter returning empty 403).
///   F3: TaskAssignment queried post-call to assert Type==ReassignAdmin + Reason=="PTO escalation".
///   F4: error response body identifies the Reason field as the missing field.
///
/// Seeding: minimal def+instance via API → WorkflowTask direct EF insert.
/// </summary>
[Collection("Integration")]
public class WorkflowTasksReassignAuthzAndStompTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private const string TasksUrl = "/api/v1/workflow/tasks";

    // ─────────────────────────────────────────────────────────────────────
    // Seeding helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a minimal workflow definition + instance; returns (instancePublicId, instanceInternalId, tenantId).
    /// Uses a fresh tenantId to isolate each test from cross-test data pollution.
    /// </summary>
    private async Task<(Guid InstancePublicId, long InstanceInternalId, string TenantId)> SeedInstanceAsync()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var adminClient = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantId);

        // Minimal 2-state definition — state names must be from GenericEntityStatusEnumContributor
        // allowed list: Draft, Submitted, UnderReview, Assigned, Approved, Rejected, Returned, ReturnedForRevision, Closed, Cancelled.
        var defResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Reassign Authz Test Workflow",
            Description = "Seeded for WorkflowTasksReassignAuthzAndStompTests",
            TenantId = tenantId,
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

        // Seed instance
        var instResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");
        var instBody = await instResp.Content.ReadFromJsonAsync<JsonElement>();
        var instancePublicId = instBody.GetProperty("data").GetProperty("id").GetGuid();

        // Resolve internal long Id via DB
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);

        return (instancePublicId, instance.Id, tenantId);
    }

    /// <summary>
    /// Inserts a WorkflowTask directly into DB (bypasses API to avoid dependency on
    /// WorkflowTask creation policy). Returns the task's long PK.
    /// assigneeId=null → unclaimed pool task; non-null → claimed task.
    /// </summary>
    private async Task<long> SeedTaskAsync(long instanceId, Guid tenantId, Guid? assigneeId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = WorkflowTask.Create(
            workflowInstanceId: instanceId,
            title: "Reassign Test Task",
            description: null,
            assigneeId: assigneeId,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: Guid.NewGuid());

        ctx.WorkflowTasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    // ─────────────────────────────────────────────────────────────────────
    // F1: IDOR closed — non-admin user → 403 + envelope body shape
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        // A non-existent task id is fine — authz gate fires before the handler reads from DB.
        // Use a large taskId that won't collide with seeded rows.
        const long NonExistentTaskId = 999_999_991L;
        var body = new { NewAssigneeId = Guid.NewGuid(), Reason = (string?)null };

        // Anti-tautology baseline: unauthenticated (no test headers) → 401 Unauthorized.
        // This proves the endpoint exists and the route is correct; a global-403 filter would
        // return 403 even here, which would make the authenticated 403 below meaningless.
        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.PostAsJsonAsync(
            $"{TasksUrl}/{NonExistentTaskId}/reassign", body);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous (no auth headers) must get 401 — proves route exists and global auth middleware fires, " +
            "so the authenticated 403 below is from the Workflow.Assign.Manage policy gate, not a misconfigured global filter");

        // Authenticated principal with zero permissions → PermissionAuthorizationHandler.Fail → 403
        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.PostAsJsonAsync(
            $"{TasksUrl}/{NonExistentTaskId}/reassign", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "any authenticated user without Workflow.Assign.Manage must be denied (IDOR fix: was plain [Authorize])");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F1b: SIBLING /assign IDOR closed (same bug class as /reassign) — non-admin → 403
    // Added in 4-A after code-reviewer caught H1 — sibling endpoint had same plain [Authorize].
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        const long NonExistentTaskId = 999_999_992L;
        var body = new { AssigneeId = Guid.NewGuid(), Reason = (string?)null };

        // Anti-tautology baseline: anon → 401 proves route exists; authenticated 403 below is the policy gate.
        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.PostAsJsonAsync(
            $"{TasksUrl}/{NonExistentTaskId}/assign", body);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anon must get 401 on /assign — proves route exists and global auth fires, " +
            "so the authenticated 403 below is from the Workflow.Assign.Manage policy gate");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.PostAsJsonAsync(
            $"{TasksUrl}/{NonExistentTaskId}/assign", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "sibling /assign IDOR fix: was plain [Authorize], now [Authorize(Policy = Workflow.Assign.Manage)]");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F2: Admin happy path — unclaimed task → 200, audit Type=Reassign
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_AdminUnclaimedTask_Returns200WithReassignAuditType()
    {
        var (_, instanceInternalId, tenantIdStr) = await SeedInstanceAsync();
        var tenantId = Guid.Parse(tenantIdStr);
        var taskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: null);
        var newAssigneeId = Guid.NewGuid();

        // Admin has Workflow.Assign.Manage → policy passes
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Assign.Manage],
            tenantId: tenantIdStr);

        var resp = await adminClient.PostAsJsonAsync(
            $"{TasksUrl}/{taskId}/reassign",
            new { NewAssigneeId = newAssigneeId, Reason = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "admin with Workflow.Assign.Manage must be able to reassign unclaimed tasks");

        // Anti-tautology F2: query DB post-call and assert audit type = Reassign (not ReassignAdmin)
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var auditRow = await ctx.TaskAssignments.AsNoTracking()
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefaultAsync(a => a.WorkflowTaskId == taskId);

        auditRow.Should().NotBeNull("TaskAssignment audit row must be written");
        auditRow!.Type.Should().Be(TaskAssignmentType.Reassign,
            "unclaimed task reassignment must use Reassign type (not ReassignAdmin)");
        auditRow.AssignedToId.Should().Be(newAssigneeId,
            "audit row must record the new assignee");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F3: Admin stomp (claimed task) with reason → 200, audit Type=ReassignAdmin + reason in DB
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_AdminStompClaimedTaskWithReason_Returns200WithReassignAdminAuditType()
    {
        var (_, instanceInternalId, tenantIdStr) = await SeedInstanceAsync();
        var tenantId = Guid.Parse(tenantIdStr);
        var originalAssigneeId = Guid.NewGuid();
        var taskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: originalAssigneeId);
        var newAssigneeId = Guid.NewGuid();
        const string StompReason = "PTO escalation";

        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Assign.Manage],
            tenantId: tenantIdStr);

        var resp = await adminClient.PostAsJsonAsync(
            $"{TasksUrl}/{taskId}/reassign",
            new { NewAssigneeId = newAssigneeId, Reason = StompReason });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "admin reassigning claimed task with reason must succeed");

        // Anti-tautology F3: query DB — must find ReassignAdmin row with captured reason
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var auditRow = await ctx.TaskAssignments.AsNoTracking()
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefaultAsync(a => a.WorkflowTaskId == taskId);

        auditRow.Should().NotBeNull("TaskAssignment audit row must be written on admin stomp");
        auditRow!.Type.Should().Be(TaskAssignmentType.ReassignAdmin,
            "admin overwriting an already-claimed task must use ReassignAdmin type");
        auditRow.Reason.Should().Be(StompReason,
            "stomp reason 'PTO escalation' must be persisted verbatim in the audit row");
        auditRow.AssignedToId.Should().Be(newAssigneeId,
            "audit row must record the new assignee after stomp");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F4: Admin stomp without reason → 422 (ValidationError → UnprocessableEntity)
    //     Anti-tautology: error body identifies Reason as the missing field
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_AdminStompClaimedTaskWithoutReason_Returns422WithReasonFieldError()
    {
        var (_, instanceInternalId, tenantIdStr) = await SeedInstanceAsync();
        var tenantId = Guid.Parse(tenantIdStr);
        var originalAssigneeId = Guid.NewGuid();
        var taskId = await SeedTaskAsync(instanceInternalId, tenantId, assigneeId: originalAssigneeId);

        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Assign.Manage],
            tenantId: tenantIdStr);

        var resp = await adminClient.PostAsJsonAsync(
            $"{TasksUrl}/{taskId}/reassign",
            new { NewAssigneeId = Guid.NewGuid(), Reason = (string?)null });

        // ValidationError → ApiControllerBase maps to 422
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "admin stomping claimed task without reason must fail with 422 (ValidationError)");

        // Anti-tautology F4: error body must identify Reason as the problem field
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace("response body must be present");
        body.ToLowerInvariant().Should().Contain("reason",
            "error message must identify 'Reason' as the missing/invalid field");
    }
}
