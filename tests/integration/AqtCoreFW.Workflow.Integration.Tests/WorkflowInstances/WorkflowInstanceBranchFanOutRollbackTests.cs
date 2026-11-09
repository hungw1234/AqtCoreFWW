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
/// A1 — Slice 6 atomic-tx rollback test (RT3-FM-F4).
///
/// Verifies that when branch fan-out staging causes a DB constraint violation during
/// <c>UpdateWithStateGuardAsync → SaveChangesAsync</c>, the entire transition is rolled back:
///   (1) HTTP 5xx or GOV_WFL_015 422 is returned.
///   (2) WorkflowInstance.CurrentStateId is unchanged (state NOT advanced).
///   (3) No orphan WorkflowTask rows are left for this instance.
///   (4) No orphan WorkflowInstanceBranchStatus rows beyond the pre-seeded conflict row.
///
/// Seam: pre-seed a <c>WorkflowInstanceBranchStatus</c> row with (InstanceId, ParallelBranchId)
/// before the transition. When ETR calls <c>UpdateWithStateGuardAsync</c>, the unique index
/// UX_WorkflowInstanceBranchStatuses_InstanceId_BranchId fires → <c>DbUpdateException</c> →
/// outer catch rolls back the own transaction (Slice 6 envelope).
///
/// Note: This tests the "outer catch re-throws unexpected exception after rollback" path.
/// The test accepts either:
///   - An exception propagated out of ExecuteTransitionAsync (unhandled → 500 in prod).
///   - A GOV_WFL_015 outcome (if a future slice maps DbUpdateException at the save layer).
/// In both cases, assert (2)-(4) prove the rollback occurred.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstanceBranchFanOutRollbackTests : IClassFixture<WebAppFixture>
{
    private const string SubmitPermission = "WORKFLOW.TEST.SUBMIT";
    private readonly WebAppFixture _fixture;

    public WorkflowInstanceBranchFanOutRollbackTests(WebAppFixture fixture)
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

    [Fact]
    public async Task BranchFanOutFailure_RollsBackTransition_StateUnchanged_NoOrphanTasks()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────
        var tenantId = Guid.NewGuid();
        var actor    = Guid.NewGuid();
        var app      = WithPermissionStub(SubmitPermission);
        _ = app.CreateClient();

        // Seed AND-branching workflow (Draft → Branching[fan-out] → Continuation → Final)
        var seeded = await InlineWorkflowDefinitionFactory.CreateWithParallelBranchAsync(
            app.Services, tenantId, actor,
            branchType: ParallelBranchType.And);

        // Authorize Submit transition (Draft → Branching) via ByPermission clause
        await InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync(
            app.Services, seeded.SubmitTransitionId, SubmitPermission);
        // Seed Continue transition (Branching → Continuation) assignment so WF_NO_HANDLER pre-check passes.
        // Branching is non-final; resolver needs at least one outgoing assignment to return non-empty pool.
        await InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync(
            app.Services, seeded.ContinueTransitionId, SubmitPermission);

        // Create a WorkflowInstance at Draft state
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

        // Pre-seed a conflicting WorkflowInstanceBranchStatus row so that SaveChanges
        // during UpdateWithStateGuardAsync hits the unique-index UX_...InstanceId_BranchId.
        await InlineWorkflowDefinitionFactory.PreSeedConflictingBranchStatusAsync(
            app.Services, instanceInternalId, seeded.ParallelBranchId);

        // Record pre-transition state counts
        int taskCountBefore;
        int branchStatusCountBefore;
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            taskCountBefore = await db.WorkflowTasks
                .IgnoreQueryFilters()
                .CountAsync(t => t.WorkflowInstanceId == instanceInternalId);
            branchStatusCountBefore = await db.InstanceBranchStatuses
                .IgnoreQueryFilters()
                .CountAsync(s => s.InstanceId == instanceInternalId);
        }

        // ── Act: attempt the transition (expects failure due to constraint violation) ──
        bool threwException = false;
        Contract.ExecuteTransitionOutcome? outcome = null;
        {
            await using var scope = app.Services.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IWorkflowModuleClient>();
            try
            {
                outcome = await client.ExecuteTransitionAsync(new Contract.ExecuteTransitionRequest(
                    InstanceId:  instancePublicId,
                    ActionName:  "Submit",
                    ActorId:     actor,
                    TenantId:    tenantId,
                    Comment:     "A1 rollback test"));
            }
            catch
            {
                // Unhandled exception from outer catch re-throw (500 path)
                threwException = true;
            }
        }

        // ── Assert ─────────────────────────────────────────────────────────────────

        // (1) Transition MUST have failed — either thrown or returned a failure outcome
        (threwException || outcome?.IsSuccess == false)
            .Should().BeTrue("branch fan-out failure must result in error or exception");

        // (1b) Non-vacuity lock: the failure must be the SaveChanges unique-index conflict (the rollback
        // seam, GOV_WFL_015), NOT an early fork-entry pool-gate rejection. Without this, asserts (2)-(4)
        // ("nothing changed") would also hold if the fork never staged — the vacuous-pass mode that the
        // child-pool factory fix closed. This pins the test to actually exercise the rollback path.
        if (!threwException)
            outcome!.ErrorCode.Should().Be("GOV_WFL_015",
                "the fork must stage then roll back on the unique-index conflict, not fail at the pool gate");

        // (2) WorkflowInstance.CurrentStateId must still be DraftStateId (NOT advanced to BranchingStateId)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var reloaded = await db.WorkflowInstances
                .IgnoreQueryFilters()
                .FirstAsync(i => i.PublicId == instancePublicId);
            reloaded.CurrentStateId.Should().Be(seeded.DraftStateId,
                "transaction rollback must restore the state to Draft (pre-transition value)");
        }

        // (3) No orphan WorkflowTask rows introduced by the failed fan-out
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var taskCountAfter = await db.WorkflowTasks
                .IgnoreQueryFilters()
                .CountAsync(t => t.WorkflowInstanceId == instanceInternalId);
            taskCountAfter.Should().Be(taskCountBefore,
                "rolled-back branch fan-out must not leave orphan WorkflowTask rows");
        }

        // (4) No additional WorkflowInstanceBranchStatus rows beyond the pre-seeded conflict
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var branchStatusCountAfter = await db.InstanceBranchStatuses
                .IgnoreQueryFilters()
                .CountAsync(s => s.InstanceId == instanceInternalId);
            branchStatusCountAfter.Should().Be(branchStatusCountBefore,
                "rolled-back branch fan-out must not leave additional WorkflowInstanceBranchStatus rows");
        }
    }

    // ── Stub identity client (mirrors WorkflowInstanceConcurrencyTests pattern) ──
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

        public Task<UserActiveStatusDto> GetUserActiveStatusAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult(new UserActiveStatusDto { IsActive = true, IsLockedOut = false });

        public Task<IReadOnlySet<string>> GetUserRoleCodesAsync(
            Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        public Task<IReadOnlySet<string>> GetUserGroupCodesAsync(
            Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

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
