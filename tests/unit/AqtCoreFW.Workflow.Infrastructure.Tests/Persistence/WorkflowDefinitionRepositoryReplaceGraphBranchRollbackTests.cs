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
/// Atomicity facts for the PARALLEL-BRANCH phase of <see cref="WorkflowDefinitionRepository.ReplaceGraphAsync"/>.
///
/// ReplaceGraphAsync issues four SaveChanges inside one TransactionScope:
///   #1 states · #2 transitions · #3 transition-assignments · #4 parallel branches (+children+assignments).
/// Preceded by four raw-SQL DELETEs (assignments, transitions, branches, states) that replace-all the graph.
///
/// The per-transition assignment suite already proves #3-throw rolls back #1+#2 (P3_A4). What was NOT
/// covered: a failure during the BRANCH phase (#4), and — critically — whether the leading raw-SQL DELETEs
/// enlist in the scope and roll back. P3_A4 used a fresh seed, so its DELETEs deleted nothing; the
/// "failed re-save wipes the definition" failure mode was never exercised. These two facts close that.
///
/// Reuses <c>ThrowOnNthSaveInterceptor</c> (declared in the per-transition assignment suite, same
/// namespace + assembly). Real SQL Server via Testcontainers; isolated DB; assertions scoped by def.Id
/// because the two facts share one database.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowDefinitionRepositoryReplaceGraphBranchRollbackTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CreatorId    = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModifierId   = new("33333333-3333-3333-3333-333333333333");

    // Branch phase = 4th SaveChanges (states, transitions, transition-assignments, branches).
    private const int BranchPhaseSaveCall = 4;

    private const string DbName = "AqtCoreFW_ReplaceGraphBranchRbTests";

    private readonly SqlServerFixture _fixture;
    private WorkflowDbContext? _ctx;

    public WorkflowDefinitionRepositoryReplaceGraphBranchRollbackTests(SqlServerFixture fixture)
        => _fixture = fixture;

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
    // Fact A: fresh insert — branch phase (#4) throws → phases 1-3 (which DID write rows) roll back.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A throw on the branch SaveChanges (#4) must roll back the states (#1), transitions (#2) and
    /// transition-assignments (#3) that were already written by the earlier SaveChanges calls.
    ///
    /// The state / transition / transition-assignment counts discriminate the realizable rollback failure:
    /// those rows reached the DB on calls #1-#3 and survive ONLY if the scope did NOT roll back. The
    /// interceptor throws in the pre-save hook of #4, so the branch rows themselves never hit the DB —
    /// their absence is confirmatory, not proof of branch atomicity (the re-save fact below carries that
    /// load — it alone exercises the leading raw-SQL DELETE enlisting in the scope).
    /// The def row is seeded outside the scope and must survive.
    /// </summary>
    [Fact]
    public async Task BranchPhaseThrows_FreshInsert_PriorPhasesRolledBack()
    {
        await using var seedCtx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(seedCtx, gen, "BranchRb-Fresh");

        await using var ctx = BuildContext(new ThrowOnNthSaveInterceptor(BranchPhaseSaveCall));
        ctx.WorkflowDefinitions.Attach(def);
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "Fork", "Review", "Done");
        // A transition WITH an assignment → #3 writes a real row (keeps the phase-3 assertion non-vacuous).
        var transitionSpecs = new[]
        {
            new NewTransitionSpec("Draft", "Fork", "Start", "Bắt đầu", "Start", null, 1, false)
            {
                Assignment = new AssignmentSpec(
                    AssignmentCombineMode.Union, false,
                    [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
            }
        };
        var branchSpecs = new[]
        {
            new NewBranchSpec("Fork", "Done", ParallelBranchType.And, null, false, 0, null, null,
                new[]
                {
                    new NewBranchChildSpec("Review", "review", "Thẩm định", null, null, 0, [])
                    {
                        Assignment = new AssignmentSpec(AssignmentCombineMode.Union, false,
                            [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
                    }
                })
        };

        var act = async () =>
            await repo.ReplaceGraphAsync(def, newStates, transitionSpecs, branchSpecs, ModifierId, CancellationToken.None);
        (await act.Should().ThrowAsync<InvalidOperationException>("interceptor forces the branch-phase SaveChanges to throw"))
            .WithMessage("*ThrowOnNthSaveInterceptor*",
                "pin the throw to the interceptor, not a name-drift resolution throw that would also be InvalidOperationException");

        await using var verify = BuildContext();

        // Discriminating: these rows were written on calls #1-#3 before #4 threw.
        (await verify.Set<WorkflowState>().CountAsync(s => s.DefinitionId == def.Id))
            .Should().Be(0, "Phase 1 states (written on #1) must roll back when the branch phase throws");
        (await verify.Set<WorkflowTransition>().CountAsync(t => t.DefinitionId == def.Id))
            .Should().Be(0, "Phase 2 transitions (written on #2) must roll back");
        (await CountTransitionAssignmentsOfDef(verify, def.Id))
            .Should().Be(0, "Phase 3 transition-assignments (written on #3) must roll back");

        // Confirmatory only — branch rows never reached the DB (pre-save throw).
        (await verify.Set<WorkflowParallelBranch>().CountAsync(b => b.DefinitionId == def.Id))
            .Should().Be(0, "no branch rows for this def");

        (await verify.WorkflowDefinitions.AnyAsync(d => d.Id == def.Id))
            .Should().BeTrue("the def row was seeded outside the scope and must survive the rollback");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Fact B: re-save — branch phase (#4) throws on a re-save → the PRE-EXISTING graph is restored.
    // This is the production-critical property: a failed re-save must not wipe the definition.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Commit a complete graph (states + one branch), then re-save a DIFFERENT graph whose branch phase
    /// (#4) throws. The original branch row must still be present with its original Id, and the original
    /// states must be intact — which holds ONLY if the leading raw-SQL DELETEs (branches, states) enlisted
    /// in the TransactionScope and rolled back. "Original branch still present" genuinely discriminates:
    /// the re-save's <c>DELETE FROM WorkflowParallelBranches</c> ran before any SaveChanges, so the row
    /// survives exclusively because that DELETE was undone. P3_A4's fresh seed never exercised this path.
    /// </summary>
    [Fact]
    public async Task BranchPhaseThrows_OnReSave_PreexistingGraphRestored()
    {
        // First save — a complete graph with a branch, committed cleanly (no interceptor).
        await using var ctx1 = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx1, gen, "BranchRb-ReSave");
        var repo1 = new WorkflowDefinitionRepository(ctx1);

        var states1 = BuildStates(def.Id, "Draft", "Fork", "Review", "Done");
        var branch1 = new[]
        {
            new NewBranchSpec("Fork", "Done", ParallelBranchType.And, null, false, 0, null, null,
                new[] { new NewBranchChildSpec("Review", "review", "Thẩm định", null, null, 0, []) })
        };
        await repo1.ReplaceGraphAsync(def, states1, [], branch1, ModifierId, CancellationToken.None);

        // Capture the original branch Id + original state names for the post-rollback existence cross-check.
        await using var mid = BuildContext();
        var originalBranchId = await mid.Set<WorkflowParallelBranch>()
            .Where(b => b.DefinitionId == def.Id)
            .Select(b => b.Id)
            .SingleAsync();
        var originalStateNames = await mid.Set<WorkflowState>()
            .Where(s => s.DefinitionId == def.Id)
            .Select(s => s.Name)
            .ToListAsync();
        originalStateNames.Should().Contain("Fork", "precondition: original graph persisted");

        // Reload the def (query-only → no SaveChanges, so the re-save's #4 stays #4) and re-attach to the
        // throwing context — mirrors the per-transition re-save pattern (P3_A2).
        var defReloaded = (await new WorkflowDefinitionRepository(mid)
            .GetWithStatesAndTransitionsAsync(def.PublicId)).Value;

        await using var ctx2 = BuildContext(new ThrowOnNthSaveInterceptor(BranchPhaseSaveCall));
        ctx2.WorkflowDefinitions.Attach(defReloaded);
        var repo2 = new WorkflowDefinitionRepository(ctx2);

        // A DIFFERENT graph (renamed states + branch) whose branch phase will throw on #4.
        var states2 = BuildStates(defReloaded.Id, "Draft", "Gate", "Audit", "End");
        var branch2 = new[]
        {
            new NewBranchSpec("Gate", "End", ParallelBranchType.And, null, false, 0, null, null,
                new[] { new NewBranchChildSpec("Audit", "audit", "Kiểm toán", null, null, 0, []) })
        };

        var act = async () =>
            await repo2.ReplaceGraphAsync(defReloaded, states2, [], branch2, ModifierId, CancellationToken.None);
        (await act.Should().ThrowAsync<InvalidOperationException>("interceptor forces the branch-phase SaveChanges to throw"))
            .WithMessage("*ThrowOnNthSaveInterceptor*",
                "pin the throw to the interceptor, not a name-drift resolution throw that would also be InvalidOperationException");

        await using var verify = BuildContext();

        // Discriminating: the original branch survives ONLY if the re-save's branch DELETE rolled back.
        (await verify.Set<WorkflowParallelBranch>().AnyAsync(b => b.Id == originalBranchId))
            .Should().BeTrue("the original branch row must be restored — its DELETE ran before #4 and must roll back");
        (await verify.Set<WorkflowParallelBranch>().CountAsync(b => b.DefinitionId == def.Id))
            .Should().Be(1, "exactly the original branch remains; the re-save's new branch never committed");

        // The original states must be intact and the re-save's renamed states must be absent.
        var statesAfter = await verify.Set<WorkflowState>()
            .Where(s => s.DefinitionId == def.Id)
            .Select(s => s.Name)
            .ToListAsync();
        statesAfter.Should().BeEquivalentTo(originalStateNames,
            "the original states must be restored — the re-save's DELETE FROM WorkflowStates must roll back");
        statesAfter.Should().NotContain("Gate", "the re-save's new state must not leak past the rollback");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static Task<int> CountTransitionAssignmentsOfDef(WorkflowDbContext ctx, long defId) =>
        ctx.Set<WorkflowTransitionAssignment>()
            .CountAsync(a => ctx.Set<WorkflowTransition>()
                .Where(t => t.DefinitionId == defId)
                .Select(t => t.Id)
                .Contains(a.WorkflowTransitionId));

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

    private static async Task<WorkflowDefinition> SeedDefinitionAsync(
        WorkflowDbContext ctx, IGuidGenerator gen, string name)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var def = WorkflowDefinition.Create(gen, $"{name}-{uniqueSuffix}", "desc", TestTenantId, CreatorId);
        ctx.WorkflowDefinitions.Add(def);
        await ctx.SaveChangesAsync();
        return def;
    }

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
