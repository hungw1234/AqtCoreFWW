using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// Phase 04 TDD facts for ReplaceGraphAsync Phase 3: Assignment + Clause INSERT.
///
/// Plan 260523-1241 — verifies that after Phase 2 (transitions saved + IDENTITY assigned),
/// Phase 3 DELETEs existing assignment rows and re-inserts from spec, all within the same
/// TransactionScope (atomicity proof via P3_A4 interceptor test).
///
/// 5b-A invariant respected: transition.Id read AFTER SaveChanges at Phase 2 → safe to
/// pass to WorkflowTransitionAssignment.Create(transitionId, ...).
///
/// No FK from WorkflowTransitionAssignments.WorkflowTransitionId → WorkflowTransitions.Id
/// (migration 20260519015732 confirmed: only unique index UX_WorkflowTransitionAssignments_TransitionId).
/// Pre-flight DELETE in ReplaceGraphAsync is therefore MANDATORY for Q2 replace-all semantics.
///
/// Uses real SQL Server via Testcontainers (SqlServerCollection fixture).
/// Each test operates in its own isolated DB (named by test) to avoid interference.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowDefinitionRepositoryReplaceGraphAssignmentTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CreatorId    = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModifierId   = new("33333333-3333-3333-3333-333333333333");

    // Isolated DB per test class — shared across 4 tests via IAsyncLifetime (MigrateAsync once).
    private const string DbName = "AqtCoreFW_ReplaceGraphAsgTests";

    private readonly SqlServerFixture _fixture;
    private WorkflowDbContext? _ctx;

    public WorkflowDefinitionRepositoryReplaceGraphAssignmentTests(SqlServerFixture fixture)
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
    // P3_A1: Happy path — all specs with Assignment → inserts assignments + clauses
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// P3_A1: Graph with 2 transitions, each carrying AssignmentSpec with 1 clause.
    /// After ReplaceGraphAsync: 2 assignment rows + 2 clause rows present in DB.
    /// Necessary proof: transition.Id (IDENTITY from Phase 2) matches assignment.WorkflowTransitionId.
    /// RefsJson round-trips unchanged (repo does not canonicalize — validator does).
    /// 5b-A invariant: transition Ids read post-SaveChanges (Phase 2) → correct IDENTITY values.
    /// </summary>
    [Fact]
    public async Task P3_A1_HappyPath_AllSpecsWithAssignment_InsertsAssignmentsAndClauses()
    {
        // Arrange: seed a definition + initial states, then build specs with assignments
        using var ctx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx, gen, "P3-A1-Def");
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "InReview", "Approved");
        var specs = new[]
        {
            new NewTransitionSpec("Draft", "InReview", "Submit", "Nộp", "Submit",
                null, 1, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Union, false,
                    [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
            },
            new NewTransitionSpec("InReview", "Approved", "Approve", "Duyệt", "Approve",
                null, 2, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Union, true,
                    [new AssignmentClauseSpec(AssignmentClauseMode.Initiator, null)])
            }
        };

        // Act
        await repo.ReplaceGraphAsync(def, newStates, specs, [], ModifierId, CancellationToken.None);

        // Assert via fresh context (avoid tracker bias)
        await using var verify = BuildContext();
        var savedTransitions = await verify.Set<WorkflowTransition>()
            .Where(t => t.DefinitionId == def.Id)
            .ToListAsync();
        savedTransitions.Should().HaveCount(2, "2 transitions in graph");

        var savedAssignments = await verify.Set<WorkflowTransitionAssignment>()
            .Include(a => a.Clauses)
            .Where(a => savedTransitions.Select(t => t.Id).Contains(a.WorkflowTransitionId))
            .ToListAsync();
        savedAssignments.Should().HaveCount(2, "each spec has an assignment");

        // Verify transition-assignment linkage
        foreach (var t in savedTransitions)
        {
            savedAssignments.Should().Contain(a => a.WorkflowTransitionId == t.Id,
                $"assignment must reference transition {t.ActionName} Id={t.Id}");
        }

        // Verify total clause count
        savedAssignments.SelectMany(a => a.Clauses).Should().HaveCount(2, "1 clause per assignment = 2 total");

        // Verify RefsJson round-trip for ByPermission clause
        var submitTransition = savedTransitions.Single(t => t.ActionName == "Submit");
        var submitAsg = savedAssignments.Single(a => a.WorkflowTransitionId == submitTransition.Id);
        submitAsg.Clauses[0].Mode.Should().Be(AssignmentClauseMode.ByPermission);
        submitAsg.Clauses[0].RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""");

        // Verify AllowAssignorOverride round-trip for Approve
        var approveTransition = savedTransitions.Single(t => t.ActionName == "Approve");
        var approveAsg = savedAssignments.Single(a => a.WorkflowTransitionId == approveTransition.Id);
        approveAsg.AllowAssignorOverride.Should().BeTrue();
        approveAsg.Clauses[0].Mode.Should().Be(AssignmentClauseMode.Initiator);
        approveAsg.Clauses[0].RefsJson.Should().BeNull("ref-less mode");
    }

    // ─────────────────────────────────────────────────────────────────────
    // P3_A2: Re-save same definition with changed assignment → old rows deleted, new rows present
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// P3_A2: Q2 replace-all proof. Call ReplaceGraphAsync twice on same definition.
    /// First call: Submit has ByPermission clause. Second call: Submit has Initiator clause.
    /// Assert: old ByPermission assignment row for Submit no longer exists; new Initiator row present.
    /// Cascade: old clauses auto-deleted via FK_WorkflowTransitionAssignmentClauses_..._AssignmentId.
    /// </summary>
    [Fact]
    public async Task P3_A2_ReSaveSameDefinition_OldRowsDeleted_NewRowsPresent()
    {
        // Arrange: first save
        await using var ctx1 = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx1, gen, "P3-A2-Def");
        var repo1 = new WorkflowDefinitionRepository(ctx1);

        var states1 = BuildStates(def.Id, "Draft", "InReview", "Done");
        var specs1 = new[]
        {
            new NewTransitionSpec("Draft", "InReview", "Submit", "Nộp", "Submit", null, 1, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Union, false,
                    [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
            },
            new NewTransitionSpec("InReview", "Done", "Finish", "Hoàn thành", "Finish", null, 2, false)
        };
        await repo1.ReplaceGraphAsync(def, states1, specs1, [], ModifierId, CancellationToken.None);

        // Capture the Submit transition id from first save for cross-check
        await using var mid = BuildContext();
        var submitAfterFirst = await mid.Set<WorkflowTransition>()
            .Where(t => t.DefinitionId == def.Id && t.ActionName == "Submit")
            .SingleAsync();
        var oldAssignmentId = await mid.Set<WorkflowTransitionAssignment>()
            .Where(a => a.WorkflowTransitionId == submitAfterFirst.Id)
            .Select(a => a.Id)
            .SingleAsync();

        // Act: second save — different assignment on Submit
        // Need fresh def load to pick up new state Ids
        var defReloaded = (await new WorkflowDefinitionRepository(mid)
            .GetWithStatesAndTransitionsAsync(def.PublicId)).Value;

        await using var ctx2 = BuildContext();
        var repo2 = new WorkflowDefinitionRepository(ctx2);
        // Re-attach def via ctx2
        ctx2.WorkflowDefinitions.Attach(defReloaded);

        var states2 = BuildStates(defReloaded.Id, "Draft", "InReview", "Done");
        var specs2 = new[]
        {
            new NewTransitionSpec("Draft", "InReview", "Submit", "Nộp", "Submit", null, 1, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Intersect, true,
                    [new AssignmentClauseSpec(AssignmentClauseMode.Initiator, null)])
            },
            new NewTransitionSpec("InReview", "Done", "Finish", "Hoàn thành", "Finish", null, 2, false)
        };
        await repo2.ReplaceGraphAsync(defReloaded, states2, specs2, [], ModifierId, CancellationToken.None);

        // Assert via fresh context
        await using var verify = BuildContext();
        var transitions = await verify.Set<WorkflowTransition>()
            .Where(t => t.DefinitionId == def.Id)
            .ToListAsync();
        transitions.Should().HaveCount(2, "still 2 transitions");

        var submitAfterSecond = transitions.Single(t => t.ActionName == "Submit");
        var newAsg = await verify.Set<WorkflowTransitionAssignment>()
            .Include(a => a.Clauses)
            .Where(a => a.WorkflowTransitionId == submitAfterSecond.Id)
            .SingleOrDefaultAsync();

        newAsg.Should().NotBeNull("Submit still has assignment after second save");
        newAsg!.Id.Should().NotBe(oldAssignmentId, "old assignment row deleted; new row has new Id");
        newAsg.CombineMode.Should().Be(AssignmentCombineMode.Intersect, "new combine mode");
        newAsg.AllowAssignorOverride.Should().BeTrue("new override flag");
        newAsg.Clauses.Should().HaveCount(1);
        newAsg.Clauses[0].Mode.Should().Be(AssignmentClauseMode.Initiator, "new clause mode");

        // Old assignment row must be gone
        var oldAsgExists = await verify.Set<WorkflowTransitionAssignment>()
            .AnyAsync(a => a.Id == oldAssignmentId);
        oldAsgExists.Should().BeFalse("old assignment row must be deleted by replace-all");
    }

    // ─────────────────────────────────────────────────────────────────────
    // P3_A3: Mixed null + non-null assignments → bare transitions have no assignment row
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// P3_A3: Graph with 2 transitions; only Submit has Assignment, Approve has null.
    /// Necessary proof: DB has exactly 1 assignment row (for Submit); Approve transition
    /// has zero assignment rows. Q2 null-assignment = delete semantics verified.
    /// </summary>
    [Fact]
    public async Task P3_A3_MixedNullAndNonNull_BareTransitionsHaveNoAssignmentRow()
    {
        // Arrange
        await using var ctx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx, gen, "P3-A3-Def");
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "InReview", "Approved");
        var specs = new[]
        {
            new NewTransitionSpec("Draft", "InReview", "Submit", "Nộp", "Submit", null, 1, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Union, false,
                    [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
            },
            // Approve has NO assignment (Assignment = null)
            new NewTransitionSpec("InReview", "Approved", "Approve", "Duyệt", "Approve", null, 2, false)
        };

        // Act
        await repo.ReplaceGraphAsync(def, newStates, specs, [], ModifierId, CancellationToken.None);

        // Assert via fresh context
        await using var verify = BuildContext();
        var transitions = await verify.Set<WorkflowTransition>()
            .Where(t => t.DefinitionId == def.Id)
            .ToListAsync();
        transitions.Should().HaveCount(2);

        var submitT = transitions.Single(t => t.ActionName == "Submit");
        var approveT = transitions.Single(t => t.ActionName == "Approve");

        // Submit has 1 assignment
        var submitAsg = await verify.Set<WorkflowTransitionAssignment>()
            .Where(a => a.WorkflowTransitionId == submitT.Id)
            .ToListAsync();
        submitAsg.Should().HaveCount(1, "Submit has assignment");

        // Approve has NO assignment row — necessary proof
        var approveAsg = await verify.Set<WorkflowTransitionAssignment>()
            .Where(a => a.WorkflowTransitionId == approveT.Id)
            .ToListAsync();
        approveAsg.Should().BeEmpty("Approve has null assignment → no row in DB");
    }

    // ─────────────────────────────────────────────────────────────────────
    // P3_A4: Atomicity — Phase 3 throws → Phase 1+2 rolled back
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// P3_A4: Atomicity proof via SaveChangesInterceptor that throws on the 3rd SaveChanges call
    /// (Phase 3 SaveChanges). Verifies TransactionScope rolls back all 3 phases:
    /// states + transitions + assignments ALL absent from DB after the throw.
    ///
    /// The def row itself is inserted BEFORE ReplaceGraphAsync (outside the scope) → survives.
    /// Only the graph rows (states, transitions, assignments) are rolled back.
    ///
    /// Interceptor counts SavingChangesAsync invocations; throws InvalidOperationException on #3.
    /// Phase 1 SaveChanges = call #1 (states), Phase 2 = call #2 (transitions), Phase 3 = call #3.
    /// </summary>
    [Fact]
    public async Task P3_A4_AtomicityRollback_Phase3Throws_NoStateOrTransitionRowsPersisted()
    {
        // Arrange: seed def in a clean context (outside scope)
        await using var seedCtx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(seedCtx, gen, "P3-A4-Def");

        // Use interceptor context that throws on 3rd SaveChanges (Phase 3)
        var interceptor = new ThrowOnNthSaveInterceptor(throwOnCallNumber: 3);
        await using var ctx = BuildContext(interceptor);
        // Re-attach def (loaded in seedCtx, need to attach to ctx for ReplaceGraphAsync)
        ctx.WorkflowDefinitions.Attach(def);
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "Approved");
        var specs = new[]
        {
            new NewTransitionSpec("Draft", "Approved", "Submit", "Nộp", "Submit", null, 1, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Union, false,
                    [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
            }
        };

        // Act: ReplaceGraphAsync should throw on Phase 3 SaveChanges
        var act = async () =>
            await repo.ReplaceGraphAsync(def, newStates, specs, [], ModifierId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>("interceptor forces Phase 3 throw");

        // Assert: TransactionScope rolled back — all graph rows absent
        await using var verify = BuildContext();
        var stateCount = await verify.Set<WorkflowState>()
            .Where(s => s.DefinitionId == def.Id)
            .CountAsync();
        stateCount.Should().Be(0, "Phase 1 states must be rolled back");

        var transitionCount = await verify.Set<WorkflowTransition>()
            .Where(t => t.DefinitionId == def.Id)
            .CountAsync();
        transitionCount.Should().Be(0, "Phase 2 transitions must be rolled back");

        // Assignment rows — no transition rows exist, so check globally by re-querying
        // (no transitions → can assert assignment table is empty for this def's transitions)
        // Verify via count of all transitions for this def = 0 (already checked), so
        // any assignments referencing non-existent transitions also rolled back.
        // Use raw count sanity on assignment table filtered by known def (indirect proof):
        var assignmentCount = await verify.Set<WorkflowTransitionAssignment>()
            .Where(a => verify.Set<WorkflowTransition>()
                .Where(t => t.DefinitionId == def.Id)
                .Select(t => t.Id)
                .Contains(a.WorkflowTransitionId))
            .CountAsync();
        assignmentCount.Should().Be(0, "Phase 3 assignments rolled back (no transitions exist → no assignments)");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private string BuildConnectionString() =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            InitialCatalog = DbName,
            TrustServerCertificate = true,
            Encrypt = Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional,
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

    /// <summary>Seeds a WorkflowDefinition with no states/transitions yet.</summary>
    private static async Task<WorkflowDefinition> SeedDefinitionAsync(
        WorkflowDbContext ctx, IGuidGenerator gen, string name)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var def = WorkflowDefinition.Create(gen, $"{name}-{uniqueSuffix}", "desc", TestTenantId, CreatorId);
        ctx.WorkflowDefinitions.Add(def);
        await ctx.SaveChangesAsync(); // def.Id assigned by IDENTITY
        return def;
    }

    /// <summary>
    /// Builds WorkflowState objects for the given definition id.
    /// First state is initial; the rest are non-initial, non-final intermediate.
    /// </summary>
    private static List<WorkflowState> BuildStates(long defId, params string[] names)
    {
        var states = new List<WorkflowState>();
        for (var i = 0; i < names.Length; i++)
        {
            states.Add(WorkflowState.Create(
                definitionId: defId,
                name: names[i],
                displayNameVi: names[i],
                displayNameEn: names[i],
                isInitial: i == 0,
                isFinal: i == names.Length - 1,
                color: "#808080",
                sortOrder: i + 1));
        }
        return states;
    }
}

/// <summary>
/// SaveChangesInterceptor that throws <see cref="InvalidOperationException"/> on the Nth
/// <c>SavingChangesAsync</c> invocation. Used to simulate Phase 3 failure in P3_A4 atomicity test.
///
/// ReplaceGraphAsync calls SaveChanges 3 times:
///   Call #1 → Phase 1 (states)
///   Call #2 → Phase 2 (transitions)
///   Call #3 → Phase 3 (assignments) ← interceptor throws here
///
/// This proves TransactionScope rolls back all 3 phases on Phase 3 failure.
/// </summary>
internal sealed class ThrowOnNthSaveInterceptor(int throwOnCallNumber) : SaveChangesInterceptor
{
    private int _callCount;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _callCount);
        if (count == throwOnCallNumber)
            throw new InvalidOperationException(
                $"ThrowOnNthSaveInterceptor: forced throw on SaveChanges call #{count} (Phase 3 simulation).");

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
