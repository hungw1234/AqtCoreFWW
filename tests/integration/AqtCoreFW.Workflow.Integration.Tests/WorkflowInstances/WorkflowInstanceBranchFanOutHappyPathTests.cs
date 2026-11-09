using AqtCoreFW.Api;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Contract = AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Workflow.Integration.Tests.WorkflowInstances;

/// <summary>
/// A2 — Slice 6 parallel-branch fan-out happy-path regression (3 facts).
///
/// Verifies that when the transition succeeds with a parallel branch configured, the atomic
/// SaveChanges persists BOTH the state advance AND the branch entities atomically:
///   (1) WorkflowInstance.CurrentStateId advances to BranchingStateId.
///   (2) WorkflowInstanceBranchStatus row is created for the instance.
///   (3) WorkflowBranchChildStatus rows exist (one per branch child).
///   (4) WorkflowTask rows exist (one per branch child) — approval tasks for the fan-out.
///
/// Tests cover AND, OR, and N-of-M join strategies (different <see cref="ParallelBranchType"/>
/// values), verifying that the atomic envelope does not regress any branch type.
///
/// No existing integration tests covered parallel-branch fan-out before Slice 6 (scout confirmed
/// no prior tests in either suite). A2 owns the regression contract at integration level.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstanceBranchFanOutHappyPathTests : IClassFixture<WebAppFixture>
{
    private const string SubmitPermission = "WORKFLOW.TEST.SUBMIT";
    private readonly WebAppFixture _fixture;

    public WorkflowInstanceBranchFanOutHappyPathTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> WithPermissionStub(params string[] permissions) =>
        _fixture.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityModuleClient>();
                services.AddScoped<IIdentityModuleClient>(_ => new StubIdentityClient(permissions));
            });
        });

    // ── shared helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a parallel-branch definition, creates a Draft instance, executes the Submit
    /// transition, and returns the internal instance id + seeded definition for assertion.
    /// </summary>
    private async Task<(
        long InstanceInternalId,
        Guid InstancePublicId,
        InlineWorkflowDefinitionFactory.SeededParallelDefinition Seeded,
        Contract.ExecuteTransitionOutcome Outcome)>
        RunBranchTransitionAsync(
            ParallelBranchType branchType,
            int? requiredCount = null)
    {
        var tenantId = Guid.NewGuid();
        var actor    = Guid.NewGuid();
        var app      = WithPermissionStub(SubmitPermission);
        _ = app.CreateClient();

        var seeded = await InlineWorkflowDefinitionFactory.CreateWithParallelBranchAsync(
            app.Services, tenantId, actor,
            branchType:    branchType,
            requiredCount: requiredCount);

        // Authorize Submit (Draft→Branching) and Continue (Branching→Continuation) transitions
        await InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync(
            app.Services, seeded.SubmitTransitionId, SubmitPermission);
        await InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync(
            app.Services, seeded.ContinueTransitionId, SubmitPermission);

        // Create WorkflowInstance at Draft state
        long instanceInternalId;
        Guid instancePublicId;
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db  = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
            var inst = WorkflowInstance.Create(gen,
                seeded.DefinitionId, seeded.DraftStateId,
                "IntegrationTest", Guid.NewGuid(), tenantId, actor);
            db.WorkflowInstances.Add(inst);
            await db.SaveChangesAsync();
            instanceInternalId = inst.Id;
            instancePublicId   = inst.PublicId;
        }

        // Execute Submit transition
        Contract.ExecuteTransitionOutcome outcome;
        {
            await using var scope = app.Services.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IWorkflowModuleClient>();
            outcome = await client.ExecuteTransitionAsync(new Contract.ExecuteTransitionRequest(
                InstanceId:  instancePublicId,
                ActionName:  "Submit",
                ActorId:     actor,
                TenantId:    tenantId,
                Comment:     $"A2 happy-path {branchType}"));
        }

        return (instanceInternalId, instancePublicId, seeded, outcome);
    }

    // ── A2a. AND-branching ───────────────────────────────────────────────────────

    [Fact]
    public async Task AndBranching_AllChildrenStaged_TransitionCommits()
    {
        var (instanceInternalId, instancePublicId, seeded, outcome) =
            await RunBranchTransitionAsync(ParallelBranchType.And);

        // Transition must succeed
        outcome.IsSuccess.Should().BeTrue(
            $"AND-branching happy path must succeed (error: {outcome.ErrorCode} — {outcome.ErrorMessage})");
        outcome.Result!.NewStateName.Should().Be("Branching");

        await AssertBranchEntitiesPersistedAsync(instanceInternalId, instancePublicId, seeded,
            expectedChildCount: 2, label: "AND");
    }

    // ── A2b. OR-branching ────────────────────────────────────────────────────────

    [Fact]
    public async Task OrBranching_ChildrenStaged_TransitionCommits()
    {
        var (instanceInternalId, instancePublicId, seeded, outcome) =
            await RunBranchTransitionAsync(ParallelBranchType.Or);

        outcome.IsSuccess.Should().BeTrue(
            $"OR-branching happy path must succeed (error: {outcome.ErrorCode} — {outcome.ErrorMessage})");
        outcome.Result!.NewStateName.Should().Be("Branching");

        await AssertBranchEntitiesPersistedAsync(instanceInternalId, instancePublicId, seeded,
            expectedChildCount: 2, label: "OR");
    }

    // ── A2c. N-of-M-branching ────────────────────────────────────────────────────

    [Fact]
    public async Task NofMBranching_ChildrenStaged_TransitionCommits()
    {
        // N=1, M=2: at least 1 approval of the 2 children needed for join
        var (instanceInternalId, instancePublicId, seeded, outcome) =
            await RunBranchTransitionAsync(ParallelBranchType.NOfM, requiredCount: 1);

        outcome.IsSuccess.Should().BeTrue(
            $"N-of-M-branching happy path must succeed (error: {outcome.ErrorCode} — {outcome.ErrorMessage})");
        outcome.Result!.NewStateName.Should().Be("Branching");

        await AssertBranchEntitiesPersistedAsync(instanceInternalId, instancePublicId, seeded,
            expectedChildCount: 2, label: "NOfM");
    }

    // ── assertion helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Common post-transition DB assertions for any parallel branch type:
    /// (1) state advanced, (2) BranchStatus created InProgress,
    /// (3) ChildStatus rows created, (4) WorkflowTask rows created.
    /// All entities persist atomically (Slice 6 single SaveChanges).
    /// </summary>
    private async Task AssertBranchEntitiesPersistedAsync(
        long instanceInternalId,
        Guid instancePublicId,
        InlineWorkflowDefinitionFactory.SeededParallelDefinition seeded,
        int expectedChildCount,
        string label)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        // (1) State advanced
        var reloaded = await db.WorkflowInstances
            .IgnoreQueryFilters()
            .FirstAsync(i => i.PublicId == instancePublicId);
        reloaded.CurrentStateId.Should().Be(seeded.BranchingStateId,
            $"[{label}] state must advance to BranchingStateId after successful fan-out");

        // (2) WorkflowInstanceBranchStatus persisted and InProgress
        var branchStatus = await db.InstanceBranchStatuses
            .IgnoreQueryFilters()
            .Include(s => s.ChildStatuses)
            .FirstOrDefaultAsync(s => s.InstanceId == instanceInternalId
                                   && s.ParallelBranchId == seeded.ParallelBranchId);
        branchStatus.Should().NotBeNull($"[{label}] WorkflowInstanceBranchStatus must be persisted atomically");
        branchStatus!.Status.Should().Be("InProgress",
            $"[{label}] branch status must be InProgress after fan-out");

        // (3) Child status rows
        branchStatus.ChildStatuses.Should().HaveCount(expectedChildCount,
            $"[{label}] one WorkflowBranchChildStatus per branch child must be persisted");

        // (4) WorkflowTask rows (approval tasks for each branch child)
        var taskCount = await db.WorkflowTasks
            .IgnoreQueryFilters()
            .CountAsync(t => t.WorkflowInstanceId == instanceInternalId);
        taskCount.Should().Be(expectedChildCount,
            $"[{label}] one WorkflowTask per branch child must be persisted atomically");
    }

    // ── Stub identity client ──────────────────────────────────────────────────────

    private sealed class StubIdentityClient(IReadOnlyCollection<string> permissions) : IIdentityModuleClient
    {
        private readonly IReadOnlySet<string> _set = permissions.ToHashSet();

        public Task<UserInfoDto?> FindByEmailAsync(string email, Guid? tenantId = null, CancellationToken ct = default)
            => Task.FromResult<UserInfoDto?>(null);

        public Task<IReadOnlyDictionary<Guid, UserInfoDto>> GetUserInfoByIdsAsync(
            IEnumerable<Guid> userIds, Guid? tenantId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, UserInfoDto>>(new Dictionary<Guid, UserInfoDto>());

        public Task<IReadOnlyDictionary<Guid, string?>> GetUserPhoneNumbersByIdsAsync(
            IEnumerable<Guid> userIds, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string?>>(new Dictionary<Guid, string?>());

        public Task<IReadOnlyList<Guid>> GetUserIdsInTenantAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlySet<string>> GetEffectivePermissionCodesAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult(_set);

        public Task<IReadOnlySet<string>> GetUserRoleCodesAsync(
            Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        public Task<IReadOnlySet<string>> GetUserGroupCodesAsync(
            Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        public Task<UserActiveStatusDto> GetUserActiveStatusAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult(new UserActiveStatusDto { IsActive = true, IsLockedOut = false });

        public Task<IReadOnlyList<Guid>> ResolveActiveUsersInGroupsAsync(
            IReadOnlyList<string> groupCodes, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlyList<Guid>> ResolveActiveUsersInRolesAsync(
            IReadOnlyList<string> roleNames, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
            IReadOnlyCollection<string> codes, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }
}
