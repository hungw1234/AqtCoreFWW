using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
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
/// End-to-end coverage for the PreviousHandler assignment producer.
///
/// PreviousHandler is a single-assignee mode: the resolver reads the most-recent completed
/// task on the instance (via the real <see cref="WorkflowTaskRepository.GetLastCompletedTaskAsync"/>
/// query against SQL Server) and assigns the next task directly to that user. Unlike the pool
/// modes, it writes a direct AssigneeId and NO WorkflowTaskCandidateIndex rows.
///
/// The existing unit suite mocks IWorkflowTaskRepository, so it cannot prove the real
/// GetLastCompletedTaskAsync query (its WHERE CompletedAt != null + ORDER BY CompletedAt DESC)
/// returns the right row. These tests drive the real command pipeline end-to-end:
/// create instance → complete the Draft task → execute the transition whose target state's
/// outgoing assignment is PreviousHandler → assert the next task is assigned to the completer.
///
/// Coverage:
///   P1 — Happy path: InReview task assignee resolves to the user who completed the Draft task;
///        task is single-assignee (CandidateRefsJson null, zero candidate-index rows).
///   P2 — Fail-closed: with no prior completed task, the PreviousHandler target resolves to an
///        empty pool → ExecuteTransition returns 422 and no next task is created.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowPreviousHandlerProducerIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Permission gating the Draft→InReview (T1) transition + the CreateInstance endpoint policy.
    private const string TaskProcessPerm = "WORKFLOW.TASK.PROCESS";
    private const string InstanceManagePerm = "WORKFLOW.INSTANCE.MANAGE";

    public WorkflowPreviousHandlerProducerIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // P1 — Happy path: PreviousHandler resolves the next task to the prior completer
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTransition_PreviousHandlerTarget_AssignsNextTaskToPriorCompletor()
    {
        var tenantId = Guid.NewGuid();
        // Three DISTINCT identities so the assertion discriminates PreviousHandler from the
        // Initiator clause mode: the initiator creates the instance AND executes the transition,
        // but a DIFFERENT user completes the prior task. PreviousHandler must resolve to the
        // completer, not the initiator/transition-executor — if it accidentally routed to the
        // Initiator branch, AssigneeId would equal initiatorId and the test would catch it.
        var initiatorId = Guid.NewGuid();
        var completerId = Guid.NewGuid();

        var def = await InlineWorkflowDefinitionFactory.CreateThreeStateWithPreviousHandlerAsync(
            _app.Services, tenantId, createdBy: initiatorId, permissionCode: TaskProcessPerm);

        var initiatorClient = CreateActorClient(initiatorId, tenantId);
        var completerClient = CreateActorClient(completerId, tenantId);

        // 1) Initiator creates the instance → factory stages the Draft task (T1 ByPermission pool).
        var instancePublicId = await CreateInstanceAsync(initiatorClient, def.DefinitionPublicId);

        long draftTaskId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var instance = await db.WorkflowInstances.FirstAsync(i => i.PublicId == instancePublicId);
            var draftTask = await db.WorkflowTasks.FirstAsync(t => t.WorkflowInstanceId == instance.Id);
            draftTaskId = draftTask.Id;
        }

        // 2) A DISTINCT user completes the Draft task → records CompletedBy=completerId, which is
        //    exactly what GetLastCompletedTaskAsync must surface to the resolver.
        var completeResp = await completerClient.PostAsync($"/api/v1/workflow/tasks/{draftTaskId}/complete", null);
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the completer must be able to complete the Draft task — sets CompletedBy used by PreviousHandler");

        // 3) Initiator executes T1 (Draft→InReview). InReview's outgoing T2 is PreviousHandler, so the
        //    factory resolves the new InReview task's assignee via the last-completed-task lookup.
        var transitionResp = await initiatorClient.PostAsJsonAsync(
            $"/api/v1/workflow/instances/{instancePublicId}/transitions",
            new { TransitionId = def.DraftToInReviewTransitionId, Comment = (string?)null });
        transitionResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "PreviousHandler resolves to the prior completer → non-empty handler → transition succeeds");

        // 4) Assert the InReview task is assigned directly to the completer, single-assignee shape.
        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var inReviewInstance = await assertDb.WorkflowInstances.FirstAsync(i => i.PublicId == instancePublicId);
        var inReviewTask = await assertDb.WorkflowTasks
            .FirstOrDefaultAsync(t => t.WorkflowInstanceId == inReviewInstance.Id && t.Id != draftTaskId);

        inReviewTask.Should().NotBeNull("ExecuteTransition must stage the InReview task");
        inReviewTask!.AssigneeId.Should().Be(completerId,
            "PreviousHandler must assign the next task to the user who COMPLETED the prior task — " +
            "proves the real GetLastCompletedTaskAsync query returns that task's CompletedBy");
        inReviewTask.AssigneeId.Should().NotBe(initiatorId,
            "completer differs from initiator/transition-executor — discriminates PreviousHandler from the Initiator clause mode");

        inReviewTask.CandidateRefsJson.Should().BeNull(
            "PreviousHandler is single-assignee — no candidate snapshot is written");

        var indexRowCount = await assertDb.WorkflowTaskCandidateIndex
            .CountAsync(r => r.WorkflowTaskId == inReviewTask.Id);
        indexRowCount.Should().Be(0,
            "single-assignee tasks must NOT materialize candidate-index rows — that path is pool-only");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // P2 — Fail-closed: no prior completed task → empty pool → 422, no next task
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTransition_PreviousHandlerTarget_NoPriorCompletedTask_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var def = await InlineWorkflowDefinitionFactory.CreateThreeStateWithPreviousHandlerAsync(
            _app.Services, tenantId, createdBy: actorId, permissionCode: TaskProcessPerm);

        var client = CreateActorClient(actorId, tenantId);

        var instancePublicId = await CreateInstanceAsync(client, def.DefinitionPublicId);

        long draftTaskId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var instance = await db.WorkflowInstances.FirstAsync(i => i.PublicId == instancePublicId);
            draftTaskId = (await db.WorkflowTasks.FirstAsync(t => t.WorkflowInstanceId == instance.Id)).Id;
        }

        // Deliberately do NOT complete the Draft task — GetLastCompletedTaskAsync returns null →
        // PreviousHandler resolves an empty pool → fail-CLOSED.
        var transitionResp = await client.PostAsJsonAsync(
            $"/api/v1/workflow/instances/{instancePublicId}/transitions",
            new { TransitionId = def.DraftToInReviewTransitionId, Comment = (string?)null });

        transitionResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "no prior completed task → empty handler pool → WF_NO_HANDLER (422), never a silent assignment");

        // The transition must be atomic: no InReview task created, instance stays at Draft.
        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instanceAfter = await assertDb.WorkflowInstances.FirstAsync(i => i.PublicId == instancePublicId);
        var otherTasks = await assertDb.WorkflowTasks
            .CountAsync(t => t.WorkflowInstanceId == instanceAfter.Id && t.Id != draftTaskId);
        otherTasks.Should().Be(0, "fail-closed transition must not stage a next task");
        instanceAfter.CurrentStateId.Should().Be(def.DraftStateId,
            "fail-closed transition must leave the instance at its initial state");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<Guid> CreateInstanceAsync(HttpClient client, Guid definitionPublicId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/workflow/instances", new
        {
            EntityType = "IntegrationTest",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "actor has WORKFLOW.INSTANCE.MANAGE — instance creation must succeed");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Single actor that can create instances, complete tasks, and execute the T1 transition.
    /// Stubs WORKFLOW.INSTANCE.MANAGE + WORKFLOW.TASK.PROCESS — the latter also satisfies the
    /// T1 ByPermission authz-of-T predicate. PreviousHandler reads no role/permission refs.
    /// </summary>
    private HttpClient CreateActorClient(Guid userId, Guid tenantId)
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
