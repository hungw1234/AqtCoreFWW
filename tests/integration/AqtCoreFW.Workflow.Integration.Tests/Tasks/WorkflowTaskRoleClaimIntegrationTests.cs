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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Workflow.Integration.Tests.Tasks;

/// <summary>
/// T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 02 integration tests.
///
/// Locks the Workflow claim/inbox read-side merge predicate
/// <c>(PrincipalType=Permission ∩ permCodes) ∪ (PrincipalType=Role ∩ roleNames)</c>
/// by seeding a pool task whose <see cref="WorkflowTaskCandidateIndex"/> contains ONLY
/// <c>PrincipalType=Role</c> rows, then varying the caller's role-name set via stubbed
/// <see cref="IIdentityModuleClient.GetUserRoleCodesAsync"/>.
///
/// PQ-1 A direct-only semantic (no group-inherited roles) is locked at the Identity
/// SQL layer (Phase 01 unit test F4 + Dapper SQL definition); these integration tests
/// assert the pipeline propagation handler → identity-client → repo → response.
///
/// Mirrors WorkflowInstancesListEndpointTests fixture pattern: WithWebHostBuilder
/// + ConfigureTestServices stubs IIdentityModuleClient + IEffectivePermissionService —
/// no AspNet identity table seeding required.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowTaskRoleClaimIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Test constants — each test seeds a fresh tenant to avoid cross-test contamination
    // on the shared Testcontainers DB.
    private const string TaskProcessPerm = "WORKFLOW.TASK.PROCESS";
    private const string RoleName = "REVIEWER";

    public WorkflowTaskRoleClaimIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────
    // I1 — Role-direct member CAN claim pool task
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_UserHasDirectRoleMatchingPool_Returns200()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedRolePoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal) { RoleName });

        var response = await client.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "user holds direct role matching the Role-pool predicate — claim must succeed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I2 — User without matching role CANNOT claim (anti-tautology lock)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_UserHasNoMatchingRole_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedRolePoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal)); // empty role set

        var response = await client.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", content: null);

        // Anti-tautology lock: proves the pool predicate uses roleNames (not "everyone passes
        // policy → claims"). User has WORKFLOW.TASK.PROCESS permission (policy passes) but no
        // matching role (handler-level pool check fails → ForbiddenError → 403).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user lacks direct role membership — handler-level pool check must produce 403");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I3 — Role-direct task appears in inbox
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_UserHasDirectRoleMatchingPool_ReturnsTask()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedRolePoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal) { RoleName });

        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("id").GetInt64() == taskId,
            "Role-pool task must appear in caller's inbox when role-name set contains pool predicate");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I4 — Inbox excludes task when role absent
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_UserHasNoMatchingRole_ExcludesTask()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedRolePoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            roleNames: new HashSet<string>(StringComparer.Ordinal)); // empty

        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();
        items.Should().NotContain(i => i.GetProperty("id").GetInt64() == taskId,
            "Role-pool task must NOT appear when caller lacks matching role — proves Role arm gated by roleNames");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a minimal pool task (Open, AssigneeId=null) with ONE WorkflowTaskCandidateIndex
    /// row of <c>PrincipalType=Role</c> / <c>PrincipalRefId="REVIEWER"</c>. Returns task.Id.
    /// No Permission-arm rows in the index — proves Permission arm is "1=0" for these tests
    /// and only the Role arm can match.
    /// </summary>
    private async Task<long> SeedRolePoolTaskAsync(Guid tenantId)
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
            title: $"Role-pool task {Guid.NewGuid():N}",
            description: null,
            assigneeId: null, // POOL task — no direct assignee
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowTasks.Add(task);
        await db.SaveChangesAsync();

        // Role-arm predicate row — no Permission-arm rows seeded for this task.
        var candidateIndex = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: task.Id,
            clauseId: 0,
            combineMode: AssignmentCombineMode.Union,
            principalType: CandidatePrincipalType.Role,
            principalRefId: RoleName, // canonical UPPER per WorkflowGraphValidator + GetUserRoleCodesAsync SQL
            tenantId: tenantId);

        db.Set<WorkflowTaskCandidateIndex>().Add(candidateIndex);
        await db.SaveChangesAsync();

        return task.Id;
    }

    /// <summary>
    /// Creates a pre-authenticated HttpClient with:
    /// (a) <see cref="IEffectivePermissionService"/> stubbed to grant WORKFLOW.TASK.PROCESS
    ///     (satisfies controller <c>[Authorize(Policy)]</c> gate);
    /// (b) <see cref="IIdentityModuleClient.GetEffectivePermissionCodesAsync"/> stubbed to
    ///     return the same WORKFLOW.TASK.PROCESS code (handler-side permission check); and
    /// (c) <see cref="IIdentityModuleClient.GetUserRoleCodesAsync"/> stubbed to return
    ///     <paramref name="roleNames"/> (drives the Role-arm of the pool predicate).
    /// Other users get empty sets (returns 403/no-claim).
    /// </summary>
    private HttpClient CreatePermittedClient(Guid userId, Guid tenantId, IReadOnlySet<string> roleNames)
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
        identityClient.GetUserRoleCodesAsync(userId, tenantId, Arg.Any<CancellationToken>())
            .Returns(roleNames);
        identityClient.GetUserRoleCodesAsync(
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
