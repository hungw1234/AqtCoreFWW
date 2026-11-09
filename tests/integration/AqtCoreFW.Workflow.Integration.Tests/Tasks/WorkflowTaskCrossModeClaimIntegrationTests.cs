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
/// RT2-15 Phase 04-FU — cross-mode candidate-index pool non-interference.
///
/// Seeds ONE <see cref="WorkflowInstance"/> with TWO pool <see cref="WorkflowTask"/>s:
/// one carrying a single <c>PrincipalType=OrgUnit</c> candidate-index row, the other a
/// single <c>PrincipalType=Group</c> row. Varies the caller's org-unit-code and
/// group-code sets via stubbed <see cref="IOrgUnitModuleClient"/> /
/// <see cref="IIdentityModuleClient"/> and asserts the read-side merge predicate
/// (<see cref="WorkflowTaskRepository.IsPoolEligibleAsync"/> /
/// <see cref="WorkflowTaskRepository.GetInboxAsync"/>) keeps the OrgUnit and Group OR-arms
/// disjoint — a caller matching one arm sees that task ONLY, never the sibling arm's task.
///
/// SCOPE / DEVIATION (per INVARIANT 260522-A audit verdict, RT2-15-PHASE-04-FU):
/// stub-based mirror of the single-mode pattern (<see cref="WorkflowTaskOrgUnitClaimIntegrationTests"/>
/// + <see cref="WorkflowTaskGroupClaimIntegrationTests"/>). The real temporal Dapper resolution
/// (active/expired/IsActive=0/canonical/cross-tenant) is already locked by
/// <c>OrgUnitModuleClientTemporalTests</c> (OU-I1..I5) at the Organization.Integration layer —
/// the row spec's literal "temporal seed" is delegated there to avoid duplicate coverage +
/// cross-module DB coupling. This test isolates the cross-mode read-side non-interference concern.
///
/// INVARIANT 260525-E: tenantId mandatory on all cross-module client calls.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowTaskCrossModeClaimIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private const string TaskProcessPerm = "WORKFLOW.TASK.PROCESS";
    private const string OrgUnitCode = "OU_PHONG_TIEP_NHAN";
    private const string GroupCode = "GROUP_TIEP_NHAN";

    public WorkflowTaskCrossModeClaimIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ─────────────────────────────────────────────────────────────────────
    // C1 — OrgUnit-only caller: sees+claims the OrgUnit task ONLY; the Group
    //      task is invisible+unclaimable (proves OR-arms do not bleed)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossMode_OrgUnitOnlyCaller_SeesOrgUnitTaskOnly()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (orgUnitTaskId, groupTaskId) = await SeedCrossModePoolTasksAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal) { OrgUnitCode },
            groupCodes: new HashSet<string>(StringComparer.Ordinal)); // no group membership

        // Inbox asserted BEFORE any claim — claiming mutates task state out of the pool.
        var inbox = await GetInboxTaskIdsAsync(client);

        inbox.Should().Contain(orgUnitTaskId,
            "caller's org-unit-code set matches the OrgUnit-arm task — it must surface in inbox");
        inbox.Should().NotContain(groupTaskId,
            "caller has no group membership — the Group-arm task must NOT leak via the OrgUnit arm");

        var claimOrgUnit = await client.PostAsync(
            $"/api/v1/workflow/tasks/{orgUnitTaskId}/claim", content: null);
        claimOrgUnit.StatusCode.Should().Be(HttpStatusCode.OK,
            "OrgUnit-arm caller may claim the OrgUnit-pool task");

        var claimGroup = await client.PostAsync(
            $"/api/v1/workflow/tasks/{groupTaskId}/claim", content: null);
        claimGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "caller lacks group membership — cross-mode non-interference must block the Group-pool claim");
    }

    // ─────────────────────────────────────────────────────────────────────
    // C2 — Caller matching BOTH arms: sees+claims both tasks (flip-on case)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossMode_BothModeCaller_SeesAndClaimsBothTasks()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (orgUnitTaskId, groupTaskId) = await SeedCrossModePoolTasksAsync(tenantId);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal) { OrgUnitCode },
            groupCodes: new HashSet<string>(StringComparer.Ordinal) { GroupCode });

        var inbox = await GetInboxTaskIdsAsync(client);

        inbox.Should().Contain(orgUnitTaskId, "caller matches the OrgUnit arm");
        inbox.Should().Contain(groupTaskId, "caller matches the Group arm — both arms visible when both match");

        var claimOrgUnit = await client.PostAsync(
            $"/api/v1/workflow/tasks/{orgUnitTaskId}/claim", content: null);
        claimOrgUnit.StatusCode.Should().Be(HttpStatusCode.OK, "OrgUnit-arm claim succeeds");

        var claimGroup = await client.PostAsync(
            $"/api/v1/workflow/tasks/{groupTaskId}/claim", content: null);
        claimGroup.StatusCode.Should().Be(HttpStatusCode.OK, "Group-arm claim succeeds for the same caller");
    }

    // ─────────────────────────────────────────────────────────────────────
    // C3 — Shared PrincipalRefId across arms: proves PrincipalType (not RefId)
    //      is the load-bearing discriminator in the read-side predicate
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossMode_SharedRefId_PrincipalTypeGatesIndependentlyOfRefId()
    {
        // C1/C2 use DISTINCT PrincipalRefId per arm (OU_PHONG_TIEP_NHAN vs GROUP_TIEP_NHAN),
        // so a passing result there could be explained by RefId divergence alone — the
        // PrincipalType column is not proven load-bearing. Here BOTH candidate-index rows carry
        // the SAME PrincipalRefId and differ ONLY by PrincipalType. An OrgUnit-only caller whose
        // org-unit-code set contains the shared ref must see the OrgUnit-arm task ONLY: the Group
        // row shares the identical RefId, so the only thing that can gate it out is PrincipalType.
        const string sharedRef = "SHARED_REF_CODE";
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (orgUnitTaskId, groupTaskId) = await SeedCrossModePoolTasksAsync(tenantId, sharedRef, sharedRef);

        var client = CreatePermittedClient(
            userId, tenantId,
            orgUnitCodes: new HashSet<string>(StringComparer.Ordinal) { sharedRef },
            groupCodes: new HashSet<string>(StringComparer.Ordinal)); // no group membership

        var inbox = await GetInboxTaskIdsAsync(client);

        inbox.Should().Contain(orgUnitTaskId,
            "the OrgUnit-arm row (PrincipalType=OrgUnit) with the shared ref matches the caller's org-unit-code set");
        inbox.Should().NotContain(groupTaskId,
            "the Group-arm row carries the SAME PrincipalRefId but PrincipalType=Group, and the caller has no "
            + "group membership — only the PrincipalType column (not RefId divergence) can gate it out");

        var claimGroup = await client.PostAsync(
            $"/api/v1/workflow/tasks/{groupTaskId}/claim", content: null);
        claimGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "identical RefId but Group PrincipalType + no group membership → the PrincipalType gate must block the claim");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds ONE <see cref="WorkflowInstance"/> + TWO pool <see cref="WorkflowTask"/>s on it:
    /// the first task carries a single <c>PrincipalType=OrgUnit</c> / <c>PrincipalRefId=OU_PHONG_TIEP_NHAN</c>
    /// candidate-index row; the second a single <c>PrincipalType=Group</c> / <c>PrincipalRefId=GROUP_TIEP_NHAN</c>
    /// row. No Permission/Role arm rows. Returns <c>(orgUnitTaskId, groupTaskId)</c>.
    /// </summary>
    private Task<(long OrgUnitTaskId, long GroupTaskId)> SeedCrossModePoolTasksAsync(Guid tenantId)
        => SeedCrossModePoolTasksAsync(tenantId, OrgUnitCode, GroupCode);

    /// <summary>
    /// Overload allowing explicit per-arm <c>PrincipalRefId</c>s — used by the C3 anti-tautology
    /// case to seed both arms with the SAME ref so the read-side filter cannot lean on RefId divergence.
    /// </summary>
    private async Task<(long OrgUnitTaskId, long GroupTaskId)> SeedCrossModePoolTasksAsync(
        Guid tenantId, string orgUnitRef, string groupRef)
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

        var orgUnitTaskId = await AddPoolTaskAsync(
            db, instance.Id, tenantId, createdBy,
            CandidatePrincipalType.OrgUnit, orgUnitRef, "OrgUnit");
        var groupTaskId = await AddPoolTaskAsync(
            db, instance.Id, tenantId, createdBy,
            CandidatePrincipalType.Group, groupRef, "Group");

        return (orgUnitTaskId, groupTaskId);
    }

    /// <summary>
    /// Adds a single pool task (Open, AssigneeId=null) to <paramref name="instanceId"/> with exactly
    /// one candidate-index row of the given arm. Returns task.Id.
    /// </summary>
    private static async Task<long> AddPoolTaskAsync(
        WorkflowDbContext db,
        long instanceId,
        Guid tenantId,
        Guid createdBy,
        CandidatePrincipalType principalType,
        string principalRefId,
        string label)
    {
        var task = WorkflowTask.Create(
            workflowInstanceId: instanceId,
            title: $"{label}-pool task {Guid.NewGuid():N}",
            description: null,
            assigneeId: null, // POOL task — no direct assignee
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowTasks.Add(task);
        await db.SaveChangesAsync();

        var candidateIndex = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: task.Id,
            clauseId: 0,
            combineMode: AssignmentCombineMode.Union,
            principalType: principalType,
            principalRefId: principalRefId, // canonical UPPER per WorkflowGraphValidator
            tenantId: tenantId);

        db.Set<WorkflowTaskCandidateIndex>().Add(candidateIndex);
        await db.SaveChangesAsync();

        return task.Id;
    }

    private static async Task<List<long>> GetInboxTaskIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/workflow/tasks/inbox");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt64())
            .ToList();
    }

    /// <summary>
    /// Creates a pre-authenticated HttpClient. <paramref name="orgUnitCodes"/> drives the OrgUnit
    /// arm (stubbed <see cref="IOrgUnitModuleClient.GetUserOrgUnitCodesAsync"/>);
    /// <paramref name="groupCodes"/> drives the Group arm (stubbed
    /// <see cref="IIdentityModuleClient.GetUserGroupCodesAsync"/>). Permission set holds
    /// WORKFLOW.TASK.PROCESS (policy passes); Role arm empty. Other callers resolve to empty sets.
    /// </summary>
    private HttpClient CreatePermittedClient(
        Guid userId,
        Guid tenantId,
        IReadOnlySet<string> orgUnitCodes,
        IReadOnlySet<string> groupCodes)
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

        // Role arm empty for all callers — only OrgUnit / Group arms exercised here.
        identityClient.GetUserRoleCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        // Group arm: controlled code set for the caller; empty for others.
        identityClient.GetUserGroupCodesAsync(userId, tenantId, Arg.Any<CancellationToken>())
            .Returns(groupCodes);
        identityClient.GetUserGroupCodesAsync(
                Arg.Is<Guid>(id => id != userId), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        // OrgUnit arm: controlled code set for the caller; empty for others.
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
