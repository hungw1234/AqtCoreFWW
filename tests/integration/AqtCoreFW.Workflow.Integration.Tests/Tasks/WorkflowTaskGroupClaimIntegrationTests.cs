using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Workflow.Integration.Tests.Tasks;

/// <summary>
/// T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP integration tests — mirror of
/// <see cref="WorkflowTaskRoleClaimIntegrationTests"/>.
///
/// Locks the Workflow claim/inbox read-side merge predicate with the Group arm:
/// <c>(PrincipalType=Group ∩ groupCodes)</c>. Seeds a pool task whose
/// <see cref="WorkflowTaskCandidateIndex"/> contains ONLY a <c>PrincipalType=Group</c> row,
/// then varies the caller's group-code set via stubbed
/// <see cref="IIdentityModuleClient.GetUserGroupCodesAsync"/>.
///
/// Direct-membership semantic locked at the Identity SQL layer (unit + integration tests
/// for <c>GetUserGroupCodesAsync</c>); these tests assert pipeline propagation
/// handler → identity-client → repo → response for the Group arm.
///
/// Closes the 260524-F producer-without-reader gate-chain for ByGroup (mirror of ByRole
/// shipped v2.137p110).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowTaskGroupClaimIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private const string TaskProcessPerm = "WORKFLOW.TASK.PROCESS";
    private const string GroupCode = "GROUP_TIEP_NHAN";

    public WorkflowTaskGroupClaimIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────
    // I1 — Group-direct member CAN claim pool task
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_UserHasDirectGroupMatchingPool_Returns200()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedGroupPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            groupCodes: new HashSet<string>(StringComparer.Ordinal) { GroupCode });

        var response = await client.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "user holds direct group membership matching the Group-pool predicate — claim must succeed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I2 — User without matching group CANNOT claim (anti-tautology lock)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_UserHasNoMatchingGroup_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedGroupPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            groupCodes: new HashSet<string>(StringComparer.Ordinal)); // empty group set

        var response = await client.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", content: null);

        // Anti-tautology lock: proves the pool predicate uses groupCodes (not "everyone passes
        // policy → claims"). User has WORKFLOW.TASK.PROCESS permission (policy passes) but no
        // matching group (handler-level pool check fails → ForbiddenError → 403).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user lacks direct group membership — handler-level pool check must produce 403");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I3 — Group-direct task appears in inbox
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_UserHasDirectGroupMatchingPool_ReturnsTask()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedGroupPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            groupCodes: new HashSet<string>(StringComparer.Ordinal) { GroupCode });

        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("id").GetInt64() == taskId,
            "Group-pool task must appear in caller's inbox when group-code set contains pool predicate");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I4 — Inbox excludes task when group absent
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_UserHasNoMatchingGroup_ExcludesTask()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedGroupPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            groupCodes: new HashSet<string>(StringComparer.Ordinal)); // empty

        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();
        items.Should().NotContain(i => i.GetProperty("id").GetInt64() == taskId,
            "Group-pool task must NOT appear when caller lacks matching group — proves Group arm gated by groupCodes");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a minimal pool task (Open, AssigneeId=null) with ONE WorkflowTaskCandidateIndex
    /// row of <c>PrincipalType=Group</c> / <c>PrincipalRefId="GROUP_TIEP_NHAN"</c>. Returns task.Id.
    /// No Permission/Role arm rows in the index — proves those arms are "1=0" for these tests
    /// and only the Group arm can match.
    /// </summary>
    private async Task<long> SeedGroupPoolTaskAsync(Guid tenantId)
    {
        var createdBy = Guid.NewGuid();
        var seeded = await InlineWorkflowDefinitionFactory.CreateAsync(
            _app.Services, tenantId, createdBy);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var instance = WorkflowInstance.Create(
            gen,
            definitionId: seeded.DefinitionId,
            initialStateId: seeded.DraftStateId,
            entityType: "IntegrationTest",
            entityId: Guid.NewGuid(),
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        var task = WorkflowTask.Create(
            workflowInstanceId: instance.Id,
            title: $"Group-pool task {Guid.NewGuid():N}",
            description: null,
            assigneeId: null, // POOL task — no direct assignee
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowTasks.Add(task);
        await db.SaveChangesAsync();

        // Group-arm predicate row — no Permission/Role arm rows seeded for this task.
        var candidateIndex = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: task.Id,
            clauseId: 0,
            combineMode: AssignmentCombineMode.Union,
            principalType: CandidatePrincipalType.Group,
            principalRefId: GroupCode, // canonical UPPER per WorkflowGraphValidator + GetUserGroupCodesAsync SQL
            tenantId: tenantId);

        db.Set<WorkflowTaskCandidateIndex>().Add(candidateIndex);
        await db.SaveChangesAsync();

        return task.Id;
    }

    /// <summary>
    /// Creates a pre-authenticated HttpClient with stubs for IEffectivePermissionService +
    /// IIdentityModuleClient. GetUserGroupCodesAsync stubbed to return <paramref name="groupCodes"/>
    /// (drives the Group-arm of the pool predicate). Role names stubbed to empty so only the
    /// Group arm can match the seeded Group-pool task.
    /// </summary>
    private HttpClient CreatePermittedClient(Guid userId, Guid tenantId, IReadOnlySet<string> groupCodes)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string> { TaskProcessPerm }
            });
        permService.GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });

        var identityClient = Substitute.For<IIdentityModuleClient>();
        identityClient.GetEffectivePermissionCodesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { TaskProcessPerm });
        identityClient.GetEffectivePermissionCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        // Role arm stubbed to empty — only Group arm can match in these tests.
        identityClient.GetUserRoleCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        identityClient.GetUserGroupCodesAsync(userId, tenantId, Arg.Any<CancellationToken>())
            .Returns(groupCodes);
        identityClient.GetUserGroupCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
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
