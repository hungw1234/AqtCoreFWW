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
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// Per-branch-child assignment-rule persistence facts for ReplaceGraphAsync (parallel-fork analogue
/// of the per-transition assignment facts). Proves a WorkflowBranchChild can carry an assignment
/// (CombineMode + ordered clauses) that survives a graph save and round-trips via EF nav, and that the
/// cascade chain (ParallelBranch → Child → Assignment → Clause) deletes the rule when the branch goes.
///
/// Uses real SQL Server via Testcontainers (SqlServerCollection fixture); each test class owns an
/// isolated DB migrated once (MigrateAsync → exercises the new additive migration end-to-end).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowDefinitionRepositoryReplaceGraphBranchChildAssignmentTests : IAsyncLifetime
{
    private static readonly Guid TestTenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CreatorId    = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModifierId   = new("33333333-3333-3333-3333-333333333333");

    private const string DbName = "AqtCoreFW_ReplaceGraphBranchAsgTests";

    private readonly SqlServerFixture _fixture;
    private WorkflowDbContext? _ctx;

    public WorkflowDefinitionRepositoryReplaceGraphBranchChildAssignmentTests(SqlServerFixture fixture)
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
    // HappyPath: child with assignment → persisted; clause ORDER preserved; flags round-trip
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildWithAssignment_Persists_ClauseOrderPreserved_FlagsRoundTrip()
    {
        await using var ctx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx, gen, "BranchAsg-Happy");
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "Fork", "LegalReview", "Done");
        var branchSpecs = new[]
        {
            new NewBranchSpec(
                BranchingStateName: "Fork",
                ContinuationStateName: "Done",
                BranchType: ParallelBranchType.And,
                RequiredCount: null,
                AllowCancelOnAnyReject: false,
                TimeoutMinutes: 0,
                PositionX: null,
                PositionY: null,
                Children: new[]
                {
                    new NewBranchChildSpec(
                        TargetStateName: "LegalReview",
                        Name: "legal-review",
                        DisplayNameVi: "Thẩm định pháp lý",
                        DepartmentId: null,
                        DefaultAssigneeId: null,
                        SortOrder: 0,
                        Resolutions: [])
                    {
                        // Two clauses in a deliberate order to prove order is preserved on read-back.
                        Assignment = new AssignmentSpec(
                            AssignmentCombineMode.Union,
                            AllowAssignorOverride: true,
                            [
                                new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]"""),
                                new AssignmentClauseSpec(AssignmentClauseMode.ByRole, """["GOV_OFFICER"]"""),
                            ])
                    }
                })
        };

        await repo.ReplaceGraphAsync(def, newStates, [], branchSpecs, ModifierId, CancellationToken.None);

        await using var verify = BuildContext();
        var child = await ChildrenOfDef(verify, def.Id)
            .Include(c => c.Assignment!).ThenInclude(a => a.Clauses)
            .SingleAsync();

        child.Assignment.Should().NotBeNull("child carries an assignment rule");
        child.Assignment!.BranchChildId.Should().Be(child.Id, "FK resolved to the persisted child IDENTITY");
        child.Assignment.CombineMode.Should().Be(AssignmentCombineMode.Union);
        child.Assignment.AllowAssignorOverride.Should().BeTrue("flag round-trips");

        child.Assignment.Clauses.Should().HaveCount(2);
        child.Assignment.Clauses[0].Mode.Should().Be(AssignmentClauseMode.ByPermission, "clause order preserved");
        child.Assignment.Clauses[0].RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""");
        child.Assignment.Clauses[1].Mode.Should().Be(AssignmentClauseMode.ByRole, "clause order preserved");
        child.Assignment.Clauses[1].RefsJson.Should().Be("""["GOV_OFFICER"]""");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cascade: re-save graph without the branch → child gone → assignment + clauses gone
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReSaveWithoutBranch_CascadeDeletes_ChildAssignmentAndClauses()
    {
        await using var ctx1 = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx1, gen, "BranchAsg-Cascade");
        var repo1 = new WorkflowDefinitionRepository(ctx1);

        var states1 = BuildStates(def.Id, "Draft", "Fork", "LegalReview", "Done");
        var branchSpecs = new[]
        {
            new NewBranchSpec("Fork", "Done", ParallelBranchType.And, null, false, 0, null, null,
                new[]
                {
                    new NewBranchChildSpec("LegalReview", "legal-review", "Thẩm định pháp lý",
                        null, null, 0, [])
                    {
                        Assignment = new AssignmentSpec(AssignmentCombineMode.Union, false,
                            [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
                    }
                })
        };
        await repo1.ReplaceGraphAsync(def, states1, [], branchSpecs, ModifierId, CancellationToken.None);

        // Capture the assignment + clause Ids before deletion for an existence cross-check.
        await using var mid = BuildContext();
        var asgId = await mid.Set<WorkflowBranchChildAssignment>()
            .Where(a => ChildrenOfDef(mid, def.Id).Select(c => c.Id).Contains(a.BranchChildId))
            .Select(a => a.Id)
            .SingleAsync();
        var clauseCount = await mid.Set<WorkflowBranchChildAssignmentClause>()
            .CountAsync(c => c.AssignmentId == asgId);
        clauseCount.Should().Be(1, "precondition: one clause persisted");

        // Re-save the graph with NO branches → ParallelBranch DELETE cascades to child → assignment → clause.
        var defReloaded = (await new WorkflowDefinitionRepository(mid)
            .GetWithStatesAndTransitionsAsync(def.PublicId)).Value;

        await using var ctx2 = BuildContext();
        ctx2.WorkflowDefinitions.Attach(defReloaded);
        var repo2 = new WorkflowDefinitionRepository(ctx2);
        var states2 = BuildStates(defReloaded.Id, "Draft", "Fork", "LegalReview", "Done");
        await repo2.ReplaceGraphAsync(defReloaded, states2, [], [], ModifierId, CancellationToken.None);

        await using var verify = BuildContext();
        (await ChildrenOfDef(verify, def.Id).AnyAsync())
            .Should().BeFalse("branch DELETE cascaded the child away");
        (await verify.Set<WorkflowBranchChildAssignment>().AnyAsync(a => a.Id == asgId))
            .Should().BeFalse("assignment cascade-deleted with the child");
        (await verify.Set<WorkflowBranchChildAssignmentClause>().AnyAsync(c => c.AssignmentId == asgId))
            .Should().BeFalse("clauses cascade-deleted with the assignment");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Null-safe: child without assignment → no assignment row, nav null
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildWithoutAssignment_NoRow_NavNull()
    {
        await using var ctx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx, gen, "BranchAsg-Null");
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "Fork", "LegalReview", "Done");
        var branchSpecs = new[]
        {
            new NewBranchSpec("Fork", "Done", ParallelBranchType.And, null, false, 0, null, null,
                new[]
                {
                    // No Assignment → bare child.
                    new NewBranchChildSpec("LegalReview", "legal-review", "Thẩm định pháp lý",
                        null, null, 0, [])
                })
        };
        await repo.ReplaceGraphAsync(def, newStates, [], branchSpecs, ModifierId, CancellationToken.None);

        await using var verify = BuildContext();
        var child = await ChildrenOfDef(verify, def.Id)
            .Include(c => c.Assignment!).ThenInclude(a => a.Clauses)
            .SingleAsync();

        child.Assignment.Should().BeNull("no assignment spec → null nav, no row");
        (await verify.Set<WorkflowBranchChildAssignment>()
                .AnyAsync(a => ChildrenOfDef(verify, def.Id).Select(c => c.Id).Contains(a.BranchChildId)))
            .Should().BeFalse("no assignment row created for this def");
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetParallelBranchesForValidationAsync: eager-loads children + assignment + clauses (Activate path).
    // Guards the RT9 trap — a hand-built validator test can't catch an eager-load miss; only a real DB
    // round-trip proves the branch graph the validator receives is fully populated (else Activate RB5 /
    // R1 exemption would silently misfire on empty branches in production).
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetParallelBranchesForValidation_EagerLoadsChildrenAssignmentAndClauses()
    {
        await using var ctx = BuildContext();
        var gen = TestGuidGenerator.Create();
        var def = await SeedDefinitionAsync(ctx, gen, "BranchAsg-Validation");
        var repo = new WorkflowDefinitionRepository(ctx);

        var newStates = BuildStates(def.Id, "Draft", "Fork", "LegalReview", "Done");
        var branchSpecs = new[]
        {
            new NewBranchSpec("Fork", "Done", ParallelBranchType.And, null, false, 0, null, null,
                new[]
                {
                    new NewBranchChildSpec("LegalReview", "legal-review", "Thẩm định pháp lý", null, null, 0, [])
                    {
                        Assignment = new AssignmentSpec(AssignmentCombineMode.Union, false,
                            [new AssignmentClauseSpec(AssignmentClauseMode.ByPermission, """["WORKFLOW.TASK.PROCESS"]""")])
                    }
                })
        };
        await repo.ReplaceGraphAsync(def, newStates, [], branchSpecs, ModifierId, CancellationToken.None);

        // Fresh context → genuinely loads from DB (not the tracked save graph).
        await using var verify = BuildContext();
        var branches = await new WorkflowDefinitionRepository(verify)
            .GetParallelBranchesForValidationAsync(def.Id, CancellationToken.None);

        branches.Should().HaveCount(1, "the saved parallel branch is returned for this definition");
        var child = branches[0].Children.Should().ContainSingle().Subject;
        child.Assignment.Should().NotBeNull(
            "eager-load must populate the child assignment — else Activate RB5 would silently pass on empty branches");
        child.Assignment!.Clauses.Should().ContainSingle()
            .Which.Mode.Should().Be(AssignmentClauseMode.ByPermission, "clauses eager-loaded too");
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

    private WorkflowDbContext BuildContext()
    {
        var optBuilder = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(BuildConnectionString(), opts =>
            {
                opts.CommandTimeout(60);
                opts.EnableRetryOnFailure(0);
            });

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

    /// <summary>
    /// Branch children belonging to a specific definition (via ParallelBranch.DefinitionId). Scopes
    /// assertions to one test's def — the 3 facts share one DB, so global queries would cross-pollute.
    /// </summary>
    private static IQueryable<WorkflowBranchChild> ChildrenOfDef(WorkflowDbContext ctx, long defId) =>
        ctx.Set<WorkflowBranchChild>()
            .Where(c => ctx.Set<WorkflowParallelBranch>()
                .Where(b => b.DefinitionId == defId)
                .Select(b => b.Id)
                .Contains(c.ParallelBranchId));

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
