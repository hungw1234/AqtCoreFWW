using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Workflow.Integration.Tests.Tasks;

/// <summary>
/// End-to-end regression guard for the pool-task candidate-index producer write path.
///
/// Each test drives the REAL producer (WorkflowTaskFactory → StageCandidateIndex) and
/// asserts that WorkflowTaskCandidateIndex rows are materialized by the factory —
/// NOT seeded manually. This file must never contain WorkflowTaskCandidateIndex.Create()
/// or db.Set&lt;WorkflowTaskCandidateIndex&gt;().Add() calls; any such call would turn
/// this guard into the exact anti-pattern that silently hid the P1 index-write gap.
///
/// Assertion structure:
///   • "before" row count = 0  (producer has not run yet — proves no pre-seeding)
///   • trigger the real producer path
///   • "after" row count ≥ 1 with EXACT PrincipalType + canonical PrincipalRefId
///
/// Coverage:
///   I1 — Path A: CreateWorkflowInstance produces ByRole index rows
///   I2 — Path B: ExecuteTransition produces ByRole index rows for the next task
///   I3 — Inbox includes a matching-role principal; excludes a non-matching principal
///   I4 — Claim succeeds for matching-role; 403 for non-matching role
///   I5 — Tenant isolation: same canonical role ref in two tenants is invisible cross-tenant (RT-3)
///   I6 — task.CandidateRefsJson is non-null (snapshot still written alongside index rows)
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowPoolTaskProducerIndexIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Canonical role name used across tests. Must be UPPER so it matches
    // the save-time Trim+ToUpperInvariant canonicalization in WorkflowGraphValidator
    // and the read-side collation-insensitive compare.
    private const string ReviewerRole = "REVIEWER";

    // Permission code that doubles as both the controller policy gate AND the T1
    // ByPermission pool predicate ref in the 3-state (path B) definition.
    private const string TaskProcessPerm = "WORKFLOW.TASK.PROCESS";

    // Permission code required by the CreateWorkflowInstance HTTP endpoint policy.
    private const string InstanceManagePerm = "WORKFLOW.INSTANCE.MANAGE";

    public WorkflowPoolTaskProducerIndexIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // I1 — Path A: CreateWorkflowInstance materializes ByRole index rows
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInstance_ByRoleTransitionAssignment_MaterializesIndexRows()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var def = await InlineWorkflowDefinitionFactory.CreateTwoStateWithRoleAssignmentAsync(
            _app.Services, tenantId, createdBy: actorId, roleName: ReviewerRole);

        // Before: zero index rows exist for any task in this tenant — proves no pre-seeding.
        var beforeCount = await CountIndexRowsForTenantAsync(tenantId);
        beforeCount.Should().Be(0, "no index rows should exist before the producer runs");

        // Drive the real producer via HTTP CreateWorkflowInstance.
        var client = CreateInstanceAndTaskClient(actorId, tenantId);
        var response = await client.PostAsJsonAsync("/api/v1/workflow/instances", new
        {
            EntityType = "IntegrationTest",
            EntityId = Guid.NewGuid(),
            DefinitionId = def.DefinitionPublicId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "actor has WORKFLOW.INSTANCE.MANAGE policy — instance creation must succeed");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var instancePublicId = body.GetProperty("data").GetProperty("id").GetGuid();

        // Retrieve the task created by the factory.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.PublicId == instancePublicId);
        instance.Should().NotBeNull("instance must be persisted");

        var task = await db.WorkflowTasks
            .FirstOrDefaultAsync(t => t.WorkflowInstanceId == instance!.Id);
        task.Should().NotBeNull("factory must have staged a task for the pool transition");
        task!.AssigneeId.Should().BeNull("pool task must have no direct assignee");

        // I1 — core assertion: index rows exist with EXACT canonical ref (anti-tautology).
        var indexRows = await db.WorkflowTaskCandidateIndex
            .Where(r => r.WorkflowTaskId == task.Id)
            .ToListAsync();

        indexRows.Should().NotBeEmpty("factory must materialize index rows — NOT manually seeded");
        indexRows.Should().AllSatisfy(r =>
        {
            r.PrincipalType.Should().Be(CandidatePrincipalType.Role);
            r.PrincipalRefId.Should().Be(ReviewerRole,
                "producer must write the canonical role name matching the ByRole clause");
            r.TenantId.Should().Be(tenantId,
                "index row tenant must match instance tenant");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // I2 — Path B: ExecuteTransition materializes ByRole index rows for next task
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTransition_ByRoleNextStateAssignment_MaterializesIndexRowsForNextTask()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        // 3-state def: T1(Draft→InReview)=ByPermission, T2(InReview→Approved)=ByRole.
        // The actor executes T1; nohResolution for InReview resolves T2's ByRole pool →
        // factory creates InReview task with ByRole index rows.
        var def = await InlineWorkflowDefinitionFactory.CreateThreeStateWithMixedAssignmentsAsync(
            _app.Services, tenantId, createdBy: actorId,
            permissionCode: TaskProcessPerm,
            roleName: ReviewerRole);

        // Seed an instance at Draft state directly via HTTP (needs InstanceManage).
        var instanceClient = CreateInstanceAndTaskClient(actorId, tenantId);
        var createResp = await instanceClient.PostAsJsonAsync("/api/v1/workflow/instances", new
        {
            EntityType = "IntegrationTest",
            EntityId = Guid.NewGuid(),
            DefinitionId = def.DefinitionPublicId,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "instance creation must succeed with WORKFLOW.INSTANCE.MANAGE");
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var instancePublicId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        // Locate the Draft task (initial state → pool from T1 ByPermission).
        // Then capture before-count of ByRole rows.
        await using var scopeA = _app.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instanceA = await dbA.WorkflowInstances.FirstAsync(i => i.PublicId == instancePublicId);
        var draftTask = await dbA.WorkflowTasks.FirstAsync(t => t.WorkflowInstanceId == instanceA.Id);

        var beforeRoleRowCount = await dbA.WorkflowTaskCandidateIndex
            .Where(r => r.TenantId == tenantId && r.PrincipalType == CandidatePrincipalType.Role)
            .CountAsync();
        beforeRoleRowCount.Should().Be(0, "no ByRole index rows should exist before ExecuteTransition");

        // Execute T1 (Draft→InReview) as actor with TaskProcessPerm — the authz gate checks
        // effectiveCodes.Contains(p.Ref) for the T1 ByPermission predicate.
        var transitionClient = CreateTransitionActorClient(actorId, tenantId);
        var transitionResp = await transitionClient.PostAsJsonAsync(
            $"/api/v1/workflow/instances/{instancePublicId}/transitions",
            new { TransitionId = def.DraftToInReviewTransitionId, Comment = (string?)null });

        transitionResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "actor has TaskProcessPerm matching T1 ByPermission clause — transition must succeed");

        // After ExecuteTransition, factory has created an InReview task.
        await using var scopeB = _app.Services.CreateAsyncScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var inReviewTask = await dbB.WorkflowTasks
            .Where(t => t.WorkflowInstanceId == instanceA.Id && t.Id != draftTask.Id)
            .FirstOrDefaultAsync();

        inReviewTask.Should().NotBeNull("factory must have staged a task for the InReview pool state");
        inReviewTask!.AssigneeId.Should().BeNull("InReview task is a pool task — no direct assignee");

        // I2 — core assertion: ByRole index rows for the InReview task.
        var inReviewIndexRows = await dbB.WorkflowTaskCandidateIndex
            .Where(r => r.WorkflowTaskId == inReviewTask.Id)
            .ToListAsync();

        inReviewIndexRows.Should().NotBeEmpty(
            "ExecuteTransition path must materialize index rows — NOT manually seeded");
        inReviewIndexRows.Should().AllSatisfy(r =>
        {
            r.PrincipalType.Should().Be(CandidatePrincipalType.Role);
            r.PrincipalRefId.Should().Be(ReviewerRole,
                "producer must write canonical role name matching the T2 ByRole clause");
            r.TenantId.Should().Be(tenantId);
        });

        // I6 — snapshot still written alongside index rows.
        inReviewTask.CandidateRefsJson.Should().NotBeNullOrEmpty(
            "CandidateRefsJson audit snapshot must be written in the same SaveChanges as index rows");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // I3 — Inbox includes matching-role; excludes non-matching role (same tenant)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_MatchingRole_IncludesTask_NonMatchingRole_ExcludesTask()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var taskId = await CreateRolePoolTaskViaProducerAsync(tenantId, actorId);

        var matchingUserId = Guid.NewGuid();
        var nonMatchingUserId = Guid.NewGuid();

        var matchingClient = CreateInboxClient(matchingUserId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal) { ReviewerRole });
        var nonMatchingClient = CreateInboxClient(nonMatchingUserId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal)); // empty role set

        // Matching principal should see the task.
        var matchingResp = await matchingClient.GetAsync("/api/v1/workflow/tasks/inbox");
        matchingResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var matchingBody = await matchingResp.Content.ReadFromJsonAsync<JsonElement>();
        matchingBody.GetProperty("data").EnumerateArray().ToList()
            .Should().Contain(i => i.GetProperty("id").GetInt64() == taskId,
                "task must appear in inbox of a principal whose role matches the pool predicate");

        // Non-matching principal must not see the task.
        var nonMatchingResp = await nonMatchingClient.GetAsync("/api/v1/workflow/tasks/inbox");
        nonMatchingResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var nonMatchingBody = await nonMatchingResp.Content.ReadFromJsonAsync<JsonElement>();
        nonMatchingBody.GetProperty("data").EnumerateArray().ToList()
            .Should().NotContain(i => i.GetProperty("id").GetInt64() == taskId,
                "task must NOT appear in inbox of a principal without the matching role — " +
                "proves pool predicate is enforced, not bypassed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // I4 — Claim 200 for matching-role; 403 for non-matching role
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_MatchingRole_Returns200_NonMatchingRole_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var taskId = await CreateRolePoolTaskViaProducerAsync(tenantId, actorId);

        var matchingUserId = Guid.NewGuid();
        var nonMatchingUserId = Guid.NewGuid();

        var matchingClient = CreateInboxClient(matchingUserId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal) { ReviewerRole });
        var nonMatchingClient = CreateInboxClient(nonMatchingUserId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal));

        var claimResp = await matchingClient.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", null);
        claimResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "user with matching role must be able to claim the pool task");

        // Re-seed because the task was claimed; create a fresh one for the 403 check.
        var taskId2 = await CreateRolePoolTaskViaProducerAsync(tenantId, actorId);
        var denyResp = await nonMatchingClient.PostAsync($"/api/v1/workflow/tasks/{taskId2}/claim", null);
        denyResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user without matching role must receive 403 — proves the pool predicate gates claim");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // I5 — Tenant isolation: same canonical ref across two tenants (RT-3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_SameRoleRefInTwoTenants_NoTaskCrossesTenantBoundary()
    {
        // Two independent tenants with colliding canonical ref string "REVIEWER".
        // A dropped TenantId predicate in WorkflowTaskRepository.GetInboxAsync would cause
        // tenantA's user to see tenantB's task and vice-versa.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actorA = Guid.NewGuid();
        var actorB = Guid.NewGuid();

        var taskIdA = await CreateRolePoolTaskViaProducerAsync(tenantA, actorA);
        var taskIdB = await CreateRolePoolTaskViaProducerAsync(tenantB, actorB);

        var userA = Guid.NewGuid(); // belongs to tenantA, has REVIEWER role
        var userB = Guid.NewGuid(); // belongs to tenantB, has REVIEWER role

        // userA: stub returns REVIEWER for tenantA but empty for tenantB.
        var clientA = CreateTenantIsolationClient(userA, tenantA, tenantB,
            ownRoleNames: new HashSet<string>(StringComparer.Ordinal) { ReviewerRole });

        // userB: stub returns REVIEWER for tenantB but empty for tenantA.
        var clientB = CreateTenantIsolationClient(userB, tenantB, tenantA,
            ownRoleNames: new HashSet<string>(StringComparer.Ordinal) { ReviewerRole });

        // userA inbox must contain taskA but NOT taskB.
        var respA = await clientA.GetAsync("/api/v1/workflow/tasks/inbox");
        respA.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyA = await respA.Content.ReadFromJsonAsync<JsonElement>();
        var itemsA = bodyA.GetProperty("data").EnumerateArray().ToList();
        itemsA.Should().Contain(i => i.GetProperty("id").GetInt64() == taskIdA,
            "userA must see tenantA task");
        itemsA.Should().NotContain(i => i.GetProperty("id").GetInt64() == taskIdB,
            "userA must NOT see tenantB task — cross-tenant leak would indicate dropped TenantId predicate");

        // userB inbox must contain taskB but NOT taskA.
        var respB = await clientB.GetAsync("/api/v1/workflow/tasks/inbox");
        respB.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyB = await respB.Content.ReadFromJsonAsync<JsonElement>();
        var itemsB = bodyB.GetProperty("data").EnumerateArray().ToList();
        itemsB.Should().Contain(i => i.GetProperty("id").GetInt64() == taskIdB,
            "userB must see tenantB task");
        itemsB.Should().NotContain(i => i.GetProperty("id").GetInt64() == taskIdA,
            "userB must NOT see tenantA task");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers — instance/task creation via the real producer (no manual index seeding)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a pool task by driving the real producer via HTTP CreateWorkflowInstance.
    /// Returns the persisted WorkflowTask.Id. Zero manual WorkflowTaskCandidateIndex rows seeded.
    /// </summary>
    private async Task<long> CreateRolePoolTaskViaProducerAsync(Guid tenantId, Guid actorId)
    {
        var def = await InlineWorkflowDefinitionFactory.CreateTwoStateWithRoleAssignmentAsync(
            _app.Services, tenantId, createdBy: actorId, roleName: ReviewerRole);

        var client = CreateInstanceAndTaskClient(actorId, tenantId);
        var response = await client.PostAsJsonAsync("/api/v1/workflow/instances", new
        {
            EntityType = "IntegrationTest",
            EntityId = Guid.NewGuid(),
            DefinitionId = def.DefinitionPublicId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var instancePublicId = body.GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await db.WorkflowInstances.FirstAsync(i => i.PublicId == instancePublicId);
        var task = await db.WorkflowTasks.FirstAsync(t => t.WorkflowInstanceId == instance.Id);
        return task.Id;
    }

    /// <summary>
    /// Count all WorkflowTaskCandidateIndex rows for any task in the given tenant.
    /// Used for the before=0 no-mock guard.
    /// </summary>
    private async Task<int> CountIndexRowsForTenantAsync(Guid tenantId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        return await db.WorkflowTaskCandidateIndex.CountAsync(r => r.TenantId == tenantId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client factories — stub configuration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Client for creating instances and executing transitions.
    /// Stubs both WORKFLOW.INSTANCE.MANAGE and WORKFLOW.TASK.PROCESS for the actor
    /// (the two policies required by POST /instances and POST /instances/{id}/transitions).
    /// </summary>
    private HttpClient CreateInstanceAndTaskClient(Guid userId, Guid tenantId)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { InstanceManagePerm, TaskProcessPerm }
            });
        permService.GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary { UserId = Guid.Empty, PermissionCodes = [] });

        var identityClient = Substitute.For<IIdentityModuleClient>();
        identityClient.GetEffectivePermissionCodesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { InstanceManagePerm, TaskProcessPerm });
        identityClient.GetEffectivePermissionCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        identityClient.GetUserRoleCodesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        return BuildClient(userId, tenantId, permService, identityClient);
    }

    /// <summary>
    /// Client for an actor executing transitions. Stubs TaskProcessPerm as both
    /// the policy gate and the T1 ByPermission predicate ref.
    /// </summary>
    private HttpClient CreateTransitionActorClient(Guid userId, Guid tenantId)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { TaskProcessPerm }
            });
        permService.GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary { UserId = Guid.Empty, PermissionCodes = [] });

        var identityClient = Substitute.For<IIdentityModuleClient>();
        identityClient.GetEffectivePermissionCodesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { TaskProcessPerm });
        identityClient.GetEffectivePermissionCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        identityClient.GetUserRoleCodesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        return BuildClient(userId, tenantId, permService, identityClient);
    }

    /// <summary>
    /// Client for inbox/claim operations. Stubs TaskProcessPerm + the supplied role names
    /// for the given userId; all other users receive empty sets.
    /// </summary>
    private HttpClient CreateInboxClient(Guid userId, Guid tenantId, IReadOnlySet<string> roleNames)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { TaskProcessPerm }
            });
        permService.GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary { UserId = Guid.Empty, PermissionCodes = [] });

        var identityClient = Substitute.For<IIdentityModuleClient>();
        identityClient.GetEffectivePermissionCodesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { TaskProcessPerm });
        identityClient.GetEffectivePermissionCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        identityClient.GetUserRoleCodesAsync(userId, tenantId, Arg.Any<CancellationToken>())
            .Returns(roleNames);
        identityClient.GetUserRoleCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        return BuildClient(userId, tenantId, permService, identityClient);
    }

    /// <summary>
    /// Client for tenant-isolation tests. Stubs roleNames for (userId, ownTenantId) only;
    /// returns empty for the other tenant so that a dropped-TenantId predicate is detectable.
    /// </summary>
    private HttpClient CreateTenantIsolationClient(
        Guid userId,
        Guid ownTenantId,
        Guid otherTenantId,
        IReadOnlySet<string> ownRoleNames)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { TaskProcessPerm }
            });
        permService.GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary { UserId = Guid.Empty, PermissionCodes = [] });

        var identityClient = Substitute.For<IIdentityModuleClient>();
        identityClient.GetEffectivePermissionCodesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { TaskProcessPerm });
        identityClient.GetEffectivePermissionCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        // Returns role names only for own tenant; empty for foreign tenant.
        identityClient.GetUserRoleCodesAsync(userId, ownTenantId, Arg.Any<CancellationToken>())
            .Returns(ownRoleNames);
        identityClient.GetUserRoleCodesAsync(userId, otherTenantId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));
        identityClient.GetUserRoleCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        return BuildClient(userId, ownTenantId, permService, identityClient);
    }

    private HttpClient BuildClient(
        Guid userId,
        Guid tenantId,
        IEffectivePermissionService permService,
        IIdentityModuleClient identityClient)
    {
        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => permService);
                services.AddScoped(_ => identityClient);
            }))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }
}
