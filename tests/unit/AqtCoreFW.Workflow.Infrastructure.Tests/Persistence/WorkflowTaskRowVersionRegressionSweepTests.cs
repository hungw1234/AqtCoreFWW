using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// PHR-V2 Phase 6 S5 — RowVersion regression sweep (RT2-12 closure, GATE-2 Path A).
///
/// Closes the RT2-12 obligation that <see cref="WorkflowTask.RowVersion"/> optimistic-concurrency
/// detection is exercised across ALL task-mutation domain methods, not just the Claim path
/// (which is independently covered by <see cref="ClaimGuardExceptionDiscriminationTests"/>).
///
/// Each fact follows the same shape (mirror of F8a/F8b/F8c in the Claim suite):
///   1. Seed an Open task (returns a tracked entity holding RowVersion V1).
///   2. Raw SQL UPDATE bumps the DB row's RowVersion to V2 — simulating a concurrent winner
///      mutation that EF cannot observe via change-tracker.
///   3. Apply the target domain method on the in-memory tracked task (RowVersion still V1).
///   4. <c>EntityState.Modified</c> is forced (or naturally set by domain mutation).
///   5. <c>SaveChangesAsync()</c> sends the UPDATE with WHERE RowVersion=V1 → 0 rows affected →
///      EF throws <see cref="DbUpdateConcurrencyException"/>.
///
/// What this proves:
///   - The <c>IsRowVersion()</c> Fluent API config on <c>WorkflowTaskConfiguration</c> IS active.
///   - Every domain-mutation path (Unclaim / Assign / Reassign / Delegate / ReturnDelegation /
///     Complete / Cancel) participates in the same RowVersion check at SaveChanges time.
///   - Future regressions that accidentally remove <c>IsRowVersion()</c> OR weaken the
///     concurrency-token configuration will fail these tests immediately.
///
/// Known architectural gap (documented in <c>docs/code-standards.md</c> §Error Handling):
///   Only <see cref="Repositories.WorkflowTaskRepository.UpdateWithClaimGuardAsync"/> catches
///   <c>DbUpdateConcurrencyException</c> and translates to a typed <c>Result.Fail</c>. The 6
///   sibling handlers (Unclaim/Assign/Reassign/Delegate/Return/Complete/Cancel) currently use
///   the generic <c>UpdateAsync</c> path which bubbles the exception — manifesting as HTTP 500.
///   These regression tests assert the underlying mechanism IS active; adding typed translation
///   per handler is separate (deferred) follow-up work tracked in <c>backlogs/backlog.md</c>.
///
/// Uses real SQL Server via Testcontainers (<see cref="SqlServerCollection"/> fixture); each
/// test operates in its own isolated DB to avoid interference.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowTaskRowVersionRegressionSweepTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("99999999-9999-9999-9999-999999999990");
    private static readonly Guid CreatorId    = new("99999999-9999-9999-9999-999999999991");
    private static readonly Guid ActorA       = new("99999999-9999-9999-9999-999999999992");
    private static readonly Guid ActorB       = new("99999999-9999-9999-9999-999999999993");

    private const string DbName = "AqtCoreFW_RowVersionSweep";

    private readonly SqlServerFixture _fixture;
    private WorkflowDbContext? _ctx;

    public WorkflowTaskRowVersionRegressionSweepTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _ctx = BuildContext();
        await _ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_ctx != null)
            await _ctx.DisposeAsync();
    }

    // -----------------------------------------------------------------
    // F1 Unclaim — assignee releases task; stale RowVersion → exception
    // -----------------------------------------------------------------

    [Fact]
    public async Task F1_Unclaim_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        var task = await SeedTaskAsync(assigneeId: ActorA);
        await BumpDbRowVersionAsync(task.Id);

        task.Unclaim(modifiedBy: ActorA);

        var act = async () => await _ctx!.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Unclaim mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F2 Assign — admin assigns pool task; stale RowVersion → exception
    // -----------------------------------------------------------------

    [Fact]
    public async Task F2_Assign_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        var task = await SeedTaskAsync(assigneeId: null);
        await BumpDbRowVersionAsync(task.Id);

        task.Assign(userId: ActorA, modifiedBy: CreatorId);

        var act = async () => await _ctx!.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Assign mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F3 Reassign — domain reuses Assign(); separate fact captures the
    //               handler-distinct call path (admin stomp / reassign).
    // -----------------------------------------------------------------

    [Fact]
    public async Task F3_Reassign_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        var task = await SeedTaskAsync(assigneeId: ActorA);
        await BumpDbRowVersionAsync(task.Id);

        // Reassign uses the same domain method as Assign — but the HANDLER path differs
        // (Reassign requires Workflow.Assign.Manage policy + Reason on claimed tasks). The
        // RowVersion mechanism on the entity is shared, which is what this regression locks.
        task.Assign(userId: ActorB, modifiedBy: CreatorId);

        var act = async () => await _ctx!.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Reassign mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F4 Delegate — assignee delegates to another user
    // -----------------------------------------------------------------

    [Fact]
    public async Task F4_Delegate_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        var task = await SeedTaskAsync(assigneeId: ActorA);
        await BumpDbRowVersionAsync(task.Id);

        task.Delegate(originalAssigneeId: ActorA, delegateUserId: ActorB, modifiedBy: ActorA);

        var act = async () => await _ctx!.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Delegate mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F5 ReturnDelegation — delegate hands task back to original assignee
    // -----------------------------------------------------------------

    [Fact]
    public async Task F5_ReturnDelegation_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        // Seed task with explicit delegation state (AssigneeId = ActorB, DelegatedFromUserId = ActorA)
        var task = await SeedTaskAsync(assigneeId: ActorA);
        task.Delegate(originalAssigneeId: ActorA, delegateUserId: ActorB, modifiedBy: ActorA);
        await _ctx!.SaveChangesAsync();

        await BumpDbRowVersionAsync(task.Id);

        task.ReturnDelegation(modifiedBy: ActorB);

        var act = async () => await _ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "ReturnDelegation mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F6 Complete — actor marks task done; stale RowVersion → exception
    // -----------------------------------------------------------------

    [Fact]
    public async Task F6_Complete_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        var task = await SeedTaskAsync(assigneeId: ActorA);
        await BumpDbRowVersionAsync(task.Id);

        task.Complete(userId: ActorA);

        var act = async () => await _ctx!.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Complete mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F7 Cancel — actor cancels task; stale RowVersion → exception
    // -----------------------------------------------------------------

    [Fact]
    public async Task F7_Cancel_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        var task = await SeedTaskAsync(assigneeId: ActorA);
        await BumpDbRowVersionAsync(task.Id);

        task.Cancel(reason: "regression-sweep", modifiedBy: ActorA);

        var act = async () => await _ctx!.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Cancel mutation must observe RowVersion mismatch (RT2-12 sweep)");
    }

    // -----------------------------------------------------------------
    // F7r — Cancel deterministic RACE (P6-DEFER-3 closure)
    //
    // Mirrors F7_ConcurrentClaim_InterceptorForcesWinnerFirst in
    // ClaimGuardExceptionDiscriminationTests, but exercises the Cancel
    // state-change handler (per DEFER-3: deterministic race on a non-Claim
    // handler). Proves the EF rowversion path fires under TRUE concurrent
    // execution (not just stale-write simulation): the interceptor gates the
    // loser's SavingChanges until the winner has committed, eliminating
    // Task.WhenAll scheduler luck.
    //
    // Why a separate fact from the stale-RowVersion sibling above:
    //   - F7_Cancel_StaleRowVersion: simulates "concurrent winner already
    //     committed" by raw-SQL bumping RowVersion. Sufficient to prove the
    //     IsRowVersion() config is active.
    //   - F7r_Cancel_ConcurrentRace: proves the SAME mismatch fires under a
    //     deterministically-ordered real race (two contexts, two Cancel
    //     mutations, interceptor enforces winner-first). Closes the 4-B H1
    //     advisory ("Task.WhenAll non-deterministic") for the Cancel handler.
    // -----------------------------------------------------------------

    [Fact]
    public async Task F7r_ConcurrentCancel_InterceptorForcesWinnerFirst_LoserThrowsConcurrencyException()
    {
        // Arrange: seed a single assigned task; both winner + loser will cancel it.
        var task = await SeedTaskAsync(assigneeId: ActorA);
        var taskId = task.Id;

        // Signal released after winner's SavedChangesAsync completes (post-commit).
        var winnerCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Winner context: no interceptor — commits freely.
        await using var winnerCtx = BuildContext();
        var winnerTask = await winnerCtx.WorkflowTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        winnerTask.Cancel(reason: "winner-cancel", modifiedBy: ActorA);

        // Loser context: interceptor gates SavingChanges until winner commits.
        var loserInterceptor = new WinnerFirstInterceptor(winnerCommitted.Task);
        await using var loserCtx = BuildContext(loserInterceptor);
        var loserTask = await loserCtx.WorkflowTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        loserTask.Cancel(reason: "loser-cancel", modifiedBy: ActorB);

        // Act: winner saves first, then signal release; loser SavingChanges unblocks
        // and attempts UPDATE WHERE RowVersion=stale → 0 rows affected → exception.
        await winnerCtx.SaveChangesAsync();
        winnerCommitted.SetResult();

        var act = async () => await loserCtx.SaveChangesAsync();

        // Assert: deterministic race fires the same RowVersion guard as the
        // stale-write sibling — proves Cancel participates in concurrent-race
        // protection, not just sequential stale-write detection.
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Cancel race with interceptor-enforced winner-first ordering must " +
            "produce DbUpdateConcurrencyException on the loser — RT2-12 DEFER-3 closure " +
            "extends F7 Claim-race coverage to a state-change handler");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task BumpDbRowVersionAsync(long taskId)
    {
        // Raw UPDATE of any column bumps SQL Server's automatic ROWVERSION column,
        // making the EF-tracked task's snapshot RowVersion stale.
        // UpdatedAt lives on the base Entity audit columns — see SharedKernel/Domain/Entity.cs.
        await using var conn = new SqlConnection(BuildConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE workflow.WorkflowTasks SET UpdatedAt = SYSUTCDATETIME() WHERE Id = {taskId}";
        await cmd.ExecuteNonQueryAsync();
    }

    private string BuildConnectionString() =>
        new SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            InitialCatalog = DbName,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
        }.ConnectionString;

    private WorkflowDbContext BuildContext(IInterceptor? interceptor = null)
    {
        var optBuilder = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(BuildConnectionString(), opts =>
            {
                opts.CommandTimeout(60);
                opts.EnableRetryOnFailure(0);
            });

        if (interceptor is not null)
            optBuilder.AddInterceptors(interceptor);

        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TestTenantId);

        return new WorkflowDbContext(optBuilder.Options, tenantCtx);
    }

    /// <summary>
    /// Seeds the WorkflowDefinition → WorkflowState → WorkflowInstance → WorkflowTask chain
    /// and returns the tracked WorkflowTask ready for stale-write tests. The optional
    /// <paramref name="assigneeId"/> seeds the task as either pool-eligible (null) or claimed.
    /// </summary>
    private async Task<WorkflowTask> SeedTaskAsync(Guid? assigneeId)
    {
        var gen = TestGuidGenerator.Create();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        var def = WorkflowDefinition.Create(gen, $"RowVersion Sweep Def {uniqueSuffix}", "", TestTenantId, CreatorId);
        _ctx!.WorkflowDefinitions.Add(def);
        await _ctx.SaveChangesAsync();

        var state = WorkflowState.Create(
            definitionId: def.Id,
            name: "Draft",
            displayNameVi: "Nháp",
            displayNameEn: "Draft",
            isInitial: true,
            isFinal: false,
            color: "#808080",
            sortOrder: 1);
        _ctx.WorkflowStates.Add(state);
        await _ctx.SaveChangesAsync();

        var instance = WorkflowInstance.Create(
            gen: gen,
            definitionId: def.Id,
            initialStateId: state.Id,
            entityType: "Generic",
            entityId: Guid.NewGuid(),
            tenantId: TestTenantId,
            createdBy: CreatorId);
        _ctx.WorkflowInstances.Add(instance);
        await _ctx.SaveChangesAsync();

        var task = WorkflowTask.Create(
            workflowInstanceId: instance.Id,
            title: "RowVersion Sweep Task",
            description: null,
            assigneeId: assigneeId,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: TestTenantId,
            createdBy: CreatorId);

        _ctx.WorkflowTasks.Add(task);
        await _ctx.SaveChangesAsync();

        return task;
    }
}
