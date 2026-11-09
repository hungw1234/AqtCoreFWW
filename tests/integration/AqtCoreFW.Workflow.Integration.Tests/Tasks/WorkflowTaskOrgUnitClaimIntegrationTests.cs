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
/// RT2-15 Phase 04 integration tests — mirror of
/// <see cref="WorkflowTaskGroupClaimIntegrationTests"/>.
///
/// Locks the Workflow claim/inbox read-side merge predicate with the OrgUnit arm:
/// <c>(PrincipalType=OrgUnit ∩ orgUnitCodes)</c>. Seeds a pool task whose
/// <see cref="WorkflowTaskCandidateIndex"/> contains ONLY a <c>PrincipalType=OrgUnit</c> row,
/// then varies the caller's org-unit-code set via stubbed
/// <see cref="IOrgUnitModuleClient.GetUserOrgUnitCodesAsync"/>.
///
/// INVARIANT 260523-D: producer + reader bundled same SHA.
/// INVARIANT 260525-E: tenantId mandatory on all IOrgUnitModuleClient calls.
/// INVARIANT 260525-G: test-layer-counts mirror ByGroup twin (4 facts).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowTaskOrgUnitClaimIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private const string TaskProcessPerm = "WORKFLOW.TASK.PROCESS";
    private const string OrgUnitCode = "OU_PHONG_TIEP_NHAN";

    public WorkflowTaskOrgUnitClaimIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────
    // I1 — OrgUnit member CAN claim pool task
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_UserHasMatchingOrgUnit_Returns200()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedOrgUnitPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal) { OrgUnitCode });

        var response = await client.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "user holds active org-unit assignment matching the OrgUnit-pool predicate — claim must succeed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I2 — User without matching org unit CANNOT claim (anti-tautology lock)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_UserHasNoMatchingOrgUnit_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedOrgUnitPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal)); // empty — no org unit match

        var response = await client.PostAsync($"/api/v1/workflow/tasks/{taskId}/claim", content: null);

        // Anti-tautology lock: proves the pool predicate uses orgUnitCodes (not "everyone passes
        // policy → claims"). User has WORKFLOW.TASK.PROCESS permission (policy passes) but no
        // matching org unit (handler-level pool check fails → ForbiddenError → 403).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user lacks org-unit membership — handler-level pool check must produce 403");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I3 — OrgUnit-pool task appears in inbox
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_UserHasMatchingOrgUnit_ReturnsTask()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedOrgUnitPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal) { OrgUnitCode });

        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("id").GetInt64() == taskId,
            "OrgUnit-pool task must appear in caller's inbox when org-unit-code set contains pool predicate");
    }

    // ─────────────────────────────────────────────────────────────────────
    // I4 — Inbox excludes task when org unit absent
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbox_UserHasNoMatchingOrgUnit_ExcludesTask()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var taskId = await SeedOrgUnitPoolTaskAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal)); // empty

        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();
        items.Should().NotContain(i => i.GetProperty("id").GetInt64() == taskId,
            "OrgUnit-pool task must NOT appear when caller lacks matching org unit — proves OrgUnit arm gated by orgUnitCodes");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a minimal pool task (Open, AssigneeId=null) with ONE WorkflowTaskCandidateIndex
    /// row of <c>PrincipalType=OrgUnit</c> / <c>PrincipalRefId="OU_PHONG_TIEP_NHAN"</c>. Returns task.Id.
    /// No Permission/Role/Group arm rows seeded — only OrgUnit arm can match.
    /// </summary>
    private async Task<long> SeedOrgUnitPoolTaskAsync(Guid tenantId)
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
            title: $"OrgUnit-pool task {Guid.NewGuid():N}",
            description: null,
            assigneeId: null, // POOL task — no direct assignee
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowTasks.Add(task);
        await db.SaveChangesAsync();

        // OrgUnit-arm predicate row — no Permission/Role/Group arm rows seeded for this task.
        var candidateIndex = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: task.Id,
            clauseId: 0,
            combineMode: AssignmentCombineMode.Union,
            principalType: CandidatePrincipalType.OrgUnit,
            principalRefId: OrgUnitCode, // canonical UPPER per WorkflowGraphValidator + GetUserOrgUnitCodesAsync SQL
            tenantId: tenantId);

        db.Set<WorkflowTaskCandidateIndex>().Add(candidateIndex);
        await db.SaveChangesAsync();

        return task.Id;
    }

    /// <summary>
    /// Creates a pre-authenticated HttpClient with stubs for IEffectivePermissionService +
    /// IIdentityModuleClient + IOrgUnitModuleClient.
    /// GetUserOrgUnitCodesAsync stubbed to return <paramref name="orgUnitCodes"/>
    /// (drives the OrgUnit-arm of the pool predicate). Permission/Role/Group stubbed
    /// to contain WORKFLOW.TASK.PROCESS permission but empty role/group sets — only the
    /// OrgUnit arm can match the seeded OrgUnit-pool task.
    /// </summary>
    private HttpClient CreatePermittedClient(Guid userId, Guid tenantId, IReadOnlySet<string> orgUnitCodes)
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

        // Role and Group arms stubbed to empty — only OrgUnit arm can match in these tests.
        identityClient.GetUserRoleCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));
        identityClient.GetUserGroupCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        // OrgUnit arm: return controlled code set for the specific caller; empty for others.
        var orgUnitClient = Substitute.For<IOrgUnitModuleClient>();
        orgUnitClient.GetUserOrgUnitCodesAsync(userId, tenantId, Arg.Any<CancellationToken>())
            .Returns(orgUnitCodes);
        orgUnitClient.GetUserOrgUnitCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => permService);
                services.AddScoped(_ => identityClient);
                services.AddScoped(_ => orgUnitClient);
            }))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }
}
