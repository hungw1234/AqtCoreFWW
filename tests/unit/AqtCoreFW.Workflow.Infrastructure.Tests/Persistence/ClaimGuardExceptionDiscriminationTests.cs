using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
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
/// Phase 4-C TDD facts for UpdateWithClaimGuardAsync exception discrimination (M1 carry-forward).
///
/// F8: DbUpdateConcurrencyException → re-query discriminates between:
///   (a) Task in Done state    → TaskFinalState (GOV_WFL_017) — NOT AlreadyClaimed.
///   (b) Task in Cancelled state → TaskFinalState (GOV_WFL_017) — NOT AlreadyClaimed.
///   (c) Task still Active (claimed by winner) → AlreadyClaimed (GOV_WFL_016).
///
/// Anti-tautology (4-B-A): each assertion explicitly checks the GOV_WFL_017 / GOV_WFL_016
/// literal in BOTH ErrorCode and WorkflowErrorCode metadata keys — not GOV_SYS_000 fallback.
///
/// F7: ISaveChangesInterceptor-based deterministic race ordering.
///   Winner's SavedChangesAsync releases signal; loser's SavingChangesAsync awaits signal.
///   Proves the EF rowversion DbUpdateConcurrencyException path fires deterministically
///   (addresses 4-B H1 advisory: Task.WhenAll is non-deterministic in ClaimTests.F3).
///
/// Uses real SQL Server via Testcontainers (SqlServerCollection fixture).
/// Each test operates in its own isolated DB (named by test) to avoid interference.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ClaimGuardExceptionDiscriminationTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CreatorId    = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid UserA        = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid UserB        = new("88888888-8888-8888-8888-888888888888");

    private const string DbName = "AqtCoreFW_ClaimGuardDiscrimination";

    private readonly SqlServerFixture _fixture;
    private WorkflowDbContext? _ctx;

    public ClaimGuardExceptionDiscriminationTests(SqlServerFixture fixture)
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

    // ─────────────────────────────────────────────────────────────────────
    // F8a: Done → TaskFinalState (GOV_WFL_017), NOT AlreadyClaimed
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F8a: When the concurrent winner finishes the task (Status=Done) and the loser's SaveChanges
    /// sees a RowVersion mismatch, the re-query detects Done status → TaskFinalState(GOV_WFL_017).
    ///
    /// Mechanism: seed task; update status to Done via raw SQL (simulates winner completing it);
    /// then call UpdateWithClaimGuardAsync on a stale in-memory task copy (old RowVersion) →
    /// DbUpdateConcurrencyException fires → re-query → Done → TaskFinalState.
    ///
    /// Anti-tautology: assert GOV_WFL_017 in BOTH ErrorCode + WorkflowErrorCode metadata,
    /// not GOV_SYS_000 (which fires when ErrorCode key is absent).
    /// </summary>
    [Fact]
    public async Task F8a_UpdateWithClaimGuard_DoneTask_ReturnsTaskFinalStateError()
    {
        var task = await SeedOpenTaskAsync();
        var taskId = task.Id;

        // Simulate winner that finishes the task: Status=Done (2), assigns UserA.
        // Raw SQL UPDATE bumps RowVersion in DB, making our EF-tracked task's RowVersion stale.
        await using var conn = new SqlConnection(BuildConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE workflow.WorkflowTasks SET Status = 2, AssigneeId = '{UserA}' WHERE Id = {taskId}";
        await cmd.ExecuteNonQueryAsync();

        // Force EF to mark task Modified so SaveChangesAsync will send an UPDATE.
        // Without this, EF sees no property changes (private setters not accessible via snapshot)
        // and SaveChanges is a no-op → no RowVersion check → no concurrency exception.
        ForceEntityModified(task);

        // Act: UpdateWithClaimGuardAsync sees stale RowVersion (DB bumped by winner's EF SaveChanges).
        // → DbUpdateConcurrencyException → re-query → Status=Done → TaskFinalState.
        var repo = new WorkflowTaskRepository(_ctx!);
        var result = await repo.UpdateWithClaimGuardAsync(task, CancellationToken.None);

        // Assert: failure — NOT success
        result.IsFailed.Should().BeTrue(
            "SaveChanges with stale RowVersion on a Done task must fail");

        var error = result.Errors.First();

        // 4-B-A INVARIANT: both metadata keys must be present and correct.
        error.Metadata.Should().ContainKey("ErrorCode",
            "4-B-A INVARIANT: ErrorCode metadata required for response body 'code' field");
        error.Metadata["ErrorCode"].Should().Be(WorkflowErrorCodes.TaskFinalState,
            "Done task must produce GOV_WFL_017 TaskFinalState, not GOV_WFL_016 AlreadyClaimed or GOV_SYS_000 fallback");

        error.Metadata.Should().ContainKey("WorkflowErrorCode",
            "4-B-A INVARIANT: WorkflowErrorCode metadata required");
        error.Metadata["WorkflowErrorCode"].Should().Be(WorkflowErrorCodes.TaskFinalState,
            "WorkflowErrorCode must also be GOV_WFL_017");

        // Prove it is NOT the AlreadyClaimed code (explicit anti-tautology).
        error.Metadata["ErrorCode"].Should().NotBe(WorkflowErrorCodes.AlreadyClaimed,
            "Done task must return TaskFinalState, not AlreadyClaimed — discrimination must work");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F8b: Cancelled → TaskFinalState (GOV_WFL_017), NOT AlreadyClaimed
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F8b: Cancelled task during race → TaskFinalState(GOV_WFL_017), same as Done.
    /// </summary>
    [Fact]
    public async Task F8b_UpdateWithClaimGuard_CancelledTask_ReturnsTaskFinalStateError()
    {
        var task = await SeedOpenTaskAsync();
        var taskId = task.Id;

        // Advance to Cancelled status (3) via raw SQL — bumps RowVersion.
        // KNOWN-FLAKE in full-suite (see F8a comment + Phase 6 carry-fwd).
        await using var conn = new SqlConnection(BuildConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE workflow.WorkflowTasks SET Status = 3, AssigneeId = '{UserA}' WHERE Id = {taskId}";
        await cmd.ExecuteNonQueryAsync();

        // Force EF to mark task Modified so SaveChangesAsync sends an UPDATE.
        ForceEntityModified(task);

        var repo = new WorkflowTaskRepository(_ctx!);
        var result = await repo.UpdateWithClaimGuardAsync(task, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.First();

        error.Metadata["ErrorCode"].Should().Be(WorkflowErrorCodes.TaskFinalState,
            "Cancelled task must produce GOV_WFL_017, not GOV_WFL_016");
        error.Metadata["WorkflowErrorCode"].Should().Be(WorkflowErrorCodes.TaskFinalState);
        error.Metadata["ErrorCode"].Should().NotBe(WorkflowErrorCodes.AlreadyClaimed);
    }

    // ─────────────────────────────────────────────────────────────────────
    // F8c: Active winner claim → AlreadyClaimed (GOV_WFL_016), NOT TaskFinalState
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F8c: When winner claims the task (sets AssigneeId, still Open) and loser's SaveChanges
    /// sees RowVersion mismatch → re-query shows Open status → AlreadyClaimed (GOV_WFL_016).
    /// This is the original pre-M1 path — must still work correctly.
    /// </summary>
    [Fact]
    public async Task F8c_UpdateWithClaimGuard_ActiveClaimedTask_ReturnsAlreadyClaimedError()
    {
        var task = await SeedOpenTaskAsync();
        var taskId = task.Id;

        // Winner claims the task — updates AssigneeId + bumps RowVersion but Status stays Open.
        await using var conn = new SqlConnection(BuildConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"UPDATE workflow.WorkflowTasks SET AssigneeId = '{UserA}' WHERE Id = {taskId}";
        await cmd.ExecuteNonQueryAsync();

        // Force EF to mark task Modified so SaveChangesAsync sends an UPDATE.
        // Loser's RowVersion is stale (winner's raw SQL bumped it); DB still has Open status.
        ForceEntityModified(task);

        var repo = new WorkflowTaskRepository(_ctx!);
        var result = await repo.UpdateWithClaimGuardAsync(task, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.First();

        error.Metadata["ErrorCode"].Should().Be(WorkflowErrorCodes.AlreadyClaimed,
            "Active claimed task must produce GOV_WFL_016 AlreadyClaimed, not GOV_WFL_017 TaskFinalState");
        error.Metadata["WorkflowErrorCode"].Should().Be(WorkflowErrorCodes.AlreadyClaimed);
        error.Metadata["ErrorCode"].Should().NotBe(WorkflowErrorCodes.TaskFinalState,
            "Still-active claimed task must NOT be treated as terminal state");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F7: Deterministic race — interceptor forces winner-before-loser ordering
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F7: ISaveChangesInterceptor enforces deterministic ordering:
    ///   Winner: SavedChangesAsync (post-commit) releases signal → RowVersion bumped in DB.
    ///   Loser:  SavingChangesAsync (pre-save) awaits signal → then attempts SaveChanges
    ///           with stale RowVersion → DbUpdateConcurrencyException → AlreadyClaimed.
    ///
    /// This proves the EF rowversion concurrency path fires deterministically, addressing
    /// 4-B H1 advisory (Task.WhenAll in integration test F3 is non-deterministic).
    ///
    /// The interceptor is injected per-test via DbContextOptionsBuilder.AddInterceptors —
    /// no DI override needed (Infrastructure.Tests tests DbContext directly, not WebApp).
    /// </summary>
    [Fact]
    public async Task F7_ConcurrentClaim_InterceptorForcesWinnerFirst_LoserGetsAlreadyClaimed()
    {
        // Seed an unclaimed pool task into the shared migrated DB.
        var task = await SeedOpenTaskAsync();
        var taskId = task.Id;

        // The signal: winner releases after its SavedChangesAsync completes.
        var winnerCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Build winner context (no interceptor — commits freely).
        await using var winnerCtx = BuildContext();
        var winnerTask = await winnerCtx.WorkflowTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == taskId);
        // Winner claims: set AssigneeId (domain path bypassed for test isolation).
        typeof(WorkflowTask)
            .GetProperty(nameof(WorkflowTask.AssigneeId))!
            .SetValue(winnerTask, UserA);

        // Build loser context WITH interceptor that gates SavingChanges until winner commits.
        var loserInterceptor = new WinnerFirstInterceptor(winnerCommitted.Task);
        await using var loserCtx = BuildContext(loserInterceptor);
        var loserTask = await loserCtx.WorkflowTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == taskId);
        typeof(WorkflowTask)
            .GetProperty(nameof(WorkflowTask.AssigneeId))!
            .SetValue(loserTask, UserB);

        // Run winner first: save, then signal.
        await winnerCtx.SaveChangesAsync();
        winnerCommitted.SetResult(); // releases loser's SavingChangesAsync gate

        // Loser: interceptor awaited signal, now SaveChanges → stale RowVersion → exception.
        var loserRepo = new WorkflowTaskRepository(loserCtx);
        var loserResult = await loserRepo.UpdateWithClaimGuardAsync(loserTask, CancellationToken.None);

        // Precondition assert: winner committed successfully (DB has UserA).
        var dbTask = await _ctx!.WorkflowTasks.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == taskId);
        dbTask.AssigneeId.Should().Be(UserA,
            "winner must have committed UserA as AssigneeId before loser's save");

        // Loser must have received AlreadyClaimed (task is still Open — active winner, not terminal).
        loserResult.IsFailed.Should().BeTrue(
            "loser must fail with DbUpdateConcurrencyException → AlreadyClaimed");

        var loserError = loserResult.Errors.First();
        loserError.Metadata["ErrorCode"].Should().Be(WorkflowErrorCodes.AlreadyClaimed,
            "concurrent claim race (active winner) → GOV_WFL_016 AlreadyClaimed, not TaskFinalState");
        loserError.Metadata["WorkflowErrorCode"].Should().Be(WorkflowErrorCodes.AlreadyClaimed);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Forces the EF context to mark the entity as Modified so SaveChangesAsync will attempt an UPDATE.
    /// Without this, SaveChangesAsync is a no-op (Unchanged entity → no SQL sent → no RowVersion check).
    /// Uses EF Entry API to mark a property as Modified — avoids relying on snapshot change detection
    /// which may not catch private-setter property changes via reflection.
    /// </summary>
    private void ForceEntityModified(WorkflowTask task)
    {
        _ctx!.Entry(task).State = EntityState.Modified;
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
    /// Seeds WorkflowDefinition → WorkflowState → WorkflowInstance → WorkflowTask chain
    /// using EF domain factories (not raw SQL) to avoid schema-drift errors on column renames.
    /// Returns the tracked WorkflowTask (with its original RowVersion) ready for
    /// UpdateWithClaimGuardAsync stale-write tests.
    /// </summary>
    private async Task<WorkflowTask> SeedOpenTaskAsync()
    {
        var gen = TestGuidGenerator.Create();

        // WorkflowDefinition — unique name per call to avoid UK_TenantId_Key_Version collision
        // when multiple tests run against the same schema (shared Testcontainers instance).
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var def = WorkflowDefinition.Create(gen, $"Discrimination Test Def {uniqueSuffix}", "", TestTenantId, CreatorId);
        _ctx!.WorkflowDefinitions.Add(def);
        await _ctx.SaveChangesAsync(); // assigns def.Id

        // WorkflowState — must have DefinitionId (just assigned above).
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
        await _ctx.SaveChangesAsync(); // assigns state.Id

        // WorkflowInstance.
        var instance = WorkflowInstance.Create(
            gen: gen,
            definitionId: def.Id,
            initialStateId: state.Id,
            entityType: "Generic",
            entityId: Guid.NewGuid(),
            tenantId: TestTenantId,
            createdBy: CreatorId);
        _ctx.WorkflowInstances.Add(instance);
        await _ctx.SaveChangesAsync(); // assigns instance.Id

        // WorkflowTask — Open, no assignee (pool task).
        var task = WorkflowTask.Create(
            workflowInstanceId: instance.Id,
            title: "Discrimination Test Task",
            description: null,
            assigneeId: null,
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: TestTenantId,
            createdBy: CreatorId);

        _ctx.WorkflowTasks.Add(task);
        await _ctx.SaveChangesAsync(); // assigns task.Id + sets RowVersion

        // Return tracked entity — EF has the original RowVersion.
        // Raw SQL bumps will make subsequent SaveChanges on this tracked instance throw
        // DbUpdateConcurrencyException (stale RowVersion).
        return task;
    }
}

/// <summary>
/// SaveChangesInterceptor that gates the loser's SavingChangesAsync until the winner's
/// SavedChangesAsync signal fires. Enforces winner-before-loser ordering deterministically.
///
/// Registration: injected via DbContextOptionsBuilder.AddInterceptors (per-context, not global DI).
/// Only loser context uses this interceptor; winner context has no interceptor.
/// </summary>
internal sealed class WinnerFirstInterceptor(Task winnerCommittedSignal) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Gate: wait until the winner has committed (RowVersion bumped in DB) before loser saves.
        // This guarantees the loser's SaveChanges sees the stale RowVersion and throws
        // DbUpdateConcurrencyException deterministically.
        await winnerCommittedSignal;
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
