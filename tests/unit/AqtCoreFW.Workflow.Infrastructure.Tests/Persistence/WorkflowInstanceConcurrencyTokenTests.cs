using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
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
/// Deterministic coverage for the EF concurrency token on WorkflowInstance.CurrentStateId
/// (WorkflowInstanceConfiguration → IsConcurrencyToken). Proves a simultaneous transition race
/// maps to StateConflict (GOV_WFL_013), distinct from the app-layer InvalidTransition guard.
///
/// Determinism: reuses WinnerFirstInterceptor (the loser's SavingChangesAsync awaits the winner's
/// commit signal) so the loser's UPDATE always runs against a CurrentStateId the winner has already
/// moved — forcing the DbUpdateConcurrencyException path every run. The prior full-stack
/// Task.WhenAll variant was probabilistic: the app-layer guard frequently won all attempts.
///
/// Mirrors ClaimGuardExceptionDiscriminationTests (same fixture + BuildContext pattern). Uses real
/// SQL Server via Testcontainers; an isolated catalog avoids interference with sibling suites.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstanceConcurrencyTokenTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CreatorId    = new("33333333-3333-3333-3333-333333333333");

    private const string DbName = "AqtCoreFW_WorkflowInstanceConcurrencyToken";

    private readonly SqlServerFixture _fixture;
    private WorkflowDbContext? _ctx;

    public WorkflowInstanceConcurrencyTokenTests(SqlServerFixture fixture)
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

    /// <summary>
    /// Winner moves CurrentStateId Draft → Submitted and commits first; the loser, loaded while the
    /// row was still Draft, then saves through WorkflowInstanceRepository.SaveChangesAsync (the ETR
    /// boundary path) → stale CurrentStateId token → DbUpdateConcurrencyException → StateConflict.
    /// </summary>
    [Fact]
    public async Task ConcurrentStateTransition_WinnerCommitsFirst_LoserGetsStateConflict()
    {
        var (instanceId, submittedStateId) = await SeedInstanceAtDraftAsync();

        // Released after the winner's SaveChangesAsync completes; gates the loser's SavingChangesAsync.
        var winnerCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Winner — no interceptor; commits the Draft → Submitted transition freely.
        await using var winnerCtx = BuildContext();
        var winnerInstance = await winnerCtx.WorkflowInstances
            .IgnoreQueryFilters().FirstAsync(i => i.Id == instanceId);
        SetCurrentState(winnerInstance, submittedStateId);
        winnerCtx.Entry(winnerInstance).Property(i => i.CurrentStateId).IsModified = true;

        // Loser — loaded while the DB still holds Draft (its concurrency snapshot is Draft), then
        // gated until the winner commits via the interceptor.
        var loserInterceptor = new WinnerFirstInterceptor(winnerCommitted.Task);
        await using var loserCtx = BuildContext(loserInterceptor);
        var loserInstance = await loserCtx.WorkflowInstances
            .IgnoreQueryFilters().FirstAsync(i => i.Id == instanceId);
        SetCurrentState(loserInstance, submittedStateId);
        loserCtx.Entry(loserInstance).Property(i => i.CurrentStateId).IsModified = true;

        // Run winner first, then release the loser's save gate.
        await winnerCtx.SaveChangesAsync();
        winnerCommitted.SetResult();

        // Loser saves against the now-stale CurrentStateId token.
        var loserRepo = new WorkflowInstanceRepository(loserCtx);
        var loserResult = await loserRepo.SaveChangesAsync();

        // Precondition: winner committed the transition before the loser saved.
        var dbInstance = await _ctx!.WorkflowInstances
            .IgnoreQueryFilters().AsNoTracking().FirstAsync(i => i.Id == instanceId);
        dbInstance.CurrentStateId.Should().Be(submittedStateId,
            "winner must commit the state transition before the loser saves");

        // Loser fails with StateConflict (GOV_WFL_013) — the DB-layer concurrency-token path.
        loserResult.IsFailed.Should().BeTrue(
            "loser's stale CurrentStateId token must trigger DbUpdateConcurrencyException → StateConflict");

        var error = loserResult.Errors.First();
        error.Metadata.Should().ContainKey("WorkflowErrorCode",
            "StateConflict errors carry the WorkflowErrorCode metadata key");
        error.Metadata["WorkflowErrorCode"].Should().Be(WorkflowErrorCodes.StateConflict,
            "IsConcurrencyToken on CurrentStateId must surface GOV_WFL_013 on a simultaneous race");
        error.Metadata["WorkflowErrorCode"].Should().NotBe(WorkflowErrorCodes.InvalidTransition,
            "the DB-layer concurrency conflict must be distinct from the app-layer sequential guard");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Reflection set for the private-setter CurrentStateId (domain entity invariant).</summary>
    private static void SetCurrentState(WorkflowInstance instance, long stateId) =>
        typeof(WorkflowInstance)
            .GetProperty(nameof(WorkflowInstance.CurrentStateId))!
            .SetValue(instance, stateId);

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
    /// Seeds WorkflowDefinition → two WorkflowStates (Draft initial + Submitted final) →
    /// WorkflowInstance at Draft, using domain factories (not raw SQL) to stay schema-drift-safe.
    /// Returns the instance long PK + the Submitted state long PK (the transition target).
    /// </summary>
    private async Task<(long instanceId, long submittedStateId)> SeedInstanceAtDraftAsync()
    {
        var gen = TestGuidGenerator.Create();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        var def = WorkflowDefinition.Create(
            gen, $"Concurrency Token Test Def {uniqueSuffix}", "", TestTenantId, CreatorId);
        _ctx!.WorkflowDefinitions.Add(def);
        await _ctx.SaveChangesAsync();

        var draft = WorkflowState.Create(
            definitionId: def.Id,
            name: "Draft",
            displayNameVi: "Nháp",
            displayNameEn: "Draft",
            isInitial: true,
            isFinal: false,
            color: "#808080",
            sortOrder: 1);
        var submitted = WorkflowState.Create(
            definitionId: def.Id,
            name: "Submitted",
            displayNameVi: "Đã nộp",
            displayNameEn: "Submitted",
            isInitial: false,
            isFinal: true,
            color: "#22aa22",
            sortOrder: 2);
        _ctx.WorkflowStates.Add(draft);
        _ctx.WorkflowStates.Add(submitted);
        await _ctx.SaveChangesAsync();

        var instance = WorkflowInstance.Create(
            gen: gen,
            definitionId: def.Id,
            initialStateId: draft.Id,
            entityType: "ConcurrencyTest",
            entityId: Guid.NewGuid(),
            tenantId: TestTenantId,
            createdBy: CreatorId);
        _ctx.WorkflowInstances.Add(instance);
        await _ctx.SaveChangesAsync();

        return (instance.Id, submitted.Id);
    }
}
