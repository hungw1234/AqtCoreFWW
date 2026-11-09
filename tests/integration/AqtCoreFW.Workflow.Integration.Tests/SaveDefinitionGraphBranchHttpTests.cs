using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// Authoring round-trip coverage for parallel branches on PUT /definitions/{id}/graph.
/// The runtime engine already executes branches — these tests prove the SAVE/READ/RE-SAVE path:
/// name-keyed replace-all persistence, branch→child→resolution cascade on delete, state-ID
/// re-resolution across re-saves (the whole reason branches are folded into ReplaceGraphAsync),
/// and the gateway-position round-trip. Build green ≠ correct: the transaction/FK behaviour is runtime.
///
/// Auth: WebAppFixture's SystemAdmin bypass allows PUT /graph (PermCodes.Workflow.Def.Manage).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SaveDefinitionGraphBranchHttpTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public SaveDefinitionGraphBranchHttpTests(WebAppFixture app) => _app = app;

    // ── Test 1 — happy round-trip: save graph + 1 branch → GET returns it fully populated ──
    [Fact]
    public async Task SaveGraph_WithBranch_RoundTrips_BranchChildrenResolutions()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var response = await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // DB shape: branch with 2 children + resolutions, FKs pointing at the freshly-saved states.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await LoadAsync(db, publicId);

        var branch = await db.ParallelBranches
            .Include(b => b.Children).ThenInclude(c => c.Resolutions)
            .AsNoTracking()
            .SingleAsync(b => b.DefinitionId == def.Id);

        var stateIdByName = def.States.ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);
        branch.BranchingStateId.Should().Be(stateIdByName[SFork]);
        branch.ContinuationStateId.Should().Be(stateIdByName[SJoin]);
        branch.Children.Should().HaveCount(2);
        branch.Children.Should().Contain(c => c.TargetStateId == stateIdByName[SChildA]);
        branch.Children.Should().Contain(c => c.TargetStateId == stateIdByName[SChildB]);
        branch.Children.SelectMany(c => c.Resolutions)
            .Should().Contain(r => r.ResultingStateId == stateIdByName[SJoin]);

        // GET read-back projection (AttachBranchesAsync) — proves the canvas reload path.
        var getBody = await (await client.GetAsync(GetUrl(publicId))).Content.ReadFromJsonAsync<JsonElement>();
        var branches = getBody.GetProperty("data").GetProperty("branches");
        branches.GetArrayLength().Should().Be(1);
        branches[0].GetProperty("children").GetArrayLength().Should().Be(2);
    }

    // ── Test 2 — replace-all delete: re-save WITHOUT the branch → cascade removes the whole tree ──
    [Fact]
    public async Task ReSaveWithoutBranch_DeletesBranch_AndCascadesChildrenResolutions()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch())).EnsureSuccessStatusCode();

        long branchId;
        long[] childIds;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await LoadAsync(db, publicId);
            var branch = await db.ParallelBranches.Include(b => b.Children).AsNoTracking()
                .SingleAsync(b => b.DefinitionId == def.Id);
            branchId = branch.Id;
            childIds = branch.Children.Select(c => c.Id).ToArray();
        }
        childIds.Should().NotBeEmpty();

        // Re-save the same graph WITHOUT branches.
        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyNoBranch())).EnsureSuccessStatusCode();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            // Branch gone + cascade fired: zero rows referencing the deleted branch/children.
            (await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM workflow.WorkflowParallelBranches WHERE Id = {0}", branchId)
                .SingleAsync()).Should().Be(0);
            (await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM workflow.WorkflowBranchChildren WHERE ParallelBranchId = {0}", branchId)
                .SingleAsync()).Should().Be(0, "branch DELETE must cascade its children");
            // The interpolated part is ONLY positional placeholders ({0},{1},…); the ids flow as
            // parameters (object[]), so there is no injection surface. EF1002 can't see that statically.
            var placeholders = string.Join(",", childIds.Select((_, i) => $"{{{i}}}"));
#pragma warning disable EF1002
            (await db.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) AS Value FROM workflow.WorkflowBranchResolutions WHERE BranchChildId IN ({placeholders})",
                childIds.Cast<object>().ToArray())
                .SingleAsync()).Should().Be(0, "child DELETE must cascade its resolutions");
#pragma warning restore EF1002
        }
    }

    // ── Test 3 — idempotent re-save: same branch twice → exactly one branch row, no unique-index 500 ──
    [Fact]
    public async Task ReSaveSameBranchTwice_IsIdempotent_OneBranchRow()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch())).EnsureSuccessStatusCode();
        var second = await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch());
        second.StatusCode.Should().Be(HttpStatusCode.OK, "replace-all re-save must not hit the unique index");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await LoadAsync(db, publicId);
        (await db.ParallelBranches.CountAsync(b => b.DefinitionId == def.Id)).Should().Be(1);
    }

    // ── Test 4 — state-ID re-resolution: re-save regenerates state IDENTITY; branch FKs follow ──
    [Fact]
    public async Task ReSaveGraph_WithBranch_BranchFksPointToNewStateIds()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch())).EnsureSuccessStatusCode();

        long firstForkId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await LoadAsync(db, publicId);
            firstForkId = def.States.Single(s => s.Name == SFork).Id;
        }

        // Re-save → states are DELETEd + re-INSERTed with fresh IDENTITY.
        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch())).EnsureSuccessStatusCode();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await LoadAsync(db, publicId);
            var newForkId = def.States.Single(s => s.Name == SFork).Id;
            newForkId.Should().NotBe(firstForkId, "replace-all assigns a new state IDENTITY");

            var branch = await db.ParallelBranches.AsNoTracking().SingleAsync(b => b.DefinitionId == def.Id);
            branch.BranchingStateId.Should().Be(newForkId,
                "name-keyed re-insert must re-resolve the branch FK to the NEW state id (no dangling reference)");
        }
    }

    // ── Test 5 — NOfM: RequiredCount=2 persists; RequiredCount null → 422 ──
    [Fact]
    public async Task SaveGraph_NOfMBranch_PersistsRequiredCount_AndRejectsNull()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch(branchType: 2, requiredCount: 2)))
            .EnsureSuccessStatusCode();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await LoadAsync(db, publicId);
            var branch = await db.ParallelBranches.AsNoTracking().SingleAsync(b => b.DefinitionId == def.Id);
            branch.RequiredCount.Should().Be(2);
        }

        var bad = await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch(branchType: 2, requiredCount: null));
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "NOfM without RequiredCount is invalid");
    }

    // ── Test 6 — backward-compat: no branches key → 200, states intact, zero branches ──
    [Fact]
    public async Task SaveGraph_WithoutBranches_Succeeds_NoBranchRows()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var response = await client.PutAsJsonAsync(GraphUrl(publicId), BodyNoBranch());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await LoadAsync(db, publicId);
        def.States.Should().HaveCount(4);
        (await db.ParallelBranches.CountAsync(b => b.DefinitionId == def.Id)).Should().Be(0);
    }

    // ── Test 7 — gateway position round-trip: set persists; null stays null ──
    [Fact]
    public async Task SaveGraph_ForkPosition_RoundTrips_AndNullStaysNull()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch(positionX: 120.5, positionY: 240.0)))
            .EnsureSuccessStatusCode();

        var withPos = (await (await client.GetAsync(GetUrl(publicId))).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("branches")[0];
        withPos.GetProperty("positionX").GetDouble().Should().Be(120.5);
        withPos.GetProperty("positionY").GetDouble().Should().Be(240.0);

        // Re-save with null position → GET returns null (FE auto-derives).
        (await client.PutAsJsonAsync(GraphUrl(publicId), BodyWithBranch())).EnsureSuccessStatusCode();
        var nullPos = (await (await client.GetAsync(GetUrl(publicId))).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("branches")[0];
        nullPos.GetProperty("positionX").ValueKind.Should().Be(JsonValueKind.Null);
        nullPos.GetProperty("positionY").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GraphUrl(Guid id) => $"/api/v1/workflow/definitions/{id}/graph";
    private static string GetUrl(Guid id) => $"/api/v1/workflow/definitions/{id}";

    private static Task<WorkflowDefinition> LoadAsync(WorkflowDbContext db, Guid publicId) =>
        db.WorkflowDefinitions.Include(d => d.States).AsNoTracking().FirstAsync(d => d.PublicId == publicId);

    // 4-state diamond (Draft → UnderReview/Assigned → Approved) with R2-valid Initiator assignments
    // on every non-final outgoing transition, so the graph validator passes and the branch is the
    // test subject. State NAMES must come from the entity-type's canonical status vocabulary
    // (Generic = [Draft, Submitted, UnderReview, Assigned, Approved, ...]); arbitrary names 422.
    private const string SFork = "Draft";        // branching (fork) state
    private const string SChildA = "UnderReview";
    private const string SChildB = "Assigned";
    private const string SJoin = "Approved";     // continuation (join) — final

    private static object[] States() =>
    [
        new { name = SFork,   displayNameVi = "Rẽ nhánh",  displayNameEn = "Fork",   isInitial = true,  isFinal = false, color = "#9E9E9E", sortOrder = 1 },
        new { name = SChildA, displayNameVi = "Việc A",    displayNameEn = "TaskA",  isInitial = false, isFinal = false, color = "#2196F3", sortOrder = 2 },
        new { name = SChildB, displayNameVi = "Việc B",    displayNameEn = "TaskB",  isInitial = false, isFinal = false, color = "#2196F3", sortOrder = 3 },
        new { name = SJoin,   displayNameVi = "Hoàn tất",  displayNameEn = "Done",   isInitial = false, isFinal = true,  color = "#4CAF50", sortOrder = 4 },
    ];

    private static object Initiator() => new
    {
        combineMode = 0,
        allowAssignorOverride = false,
        clauses = new[] { new { mode = 1, refsJson = (string?)null } },
    };

    private static object[] Transitions() =>
    [
        new { fromStateName = SFork,   toStateName = SChildA, actionName = "GoA",     actionLabelVi = "Tới A",  actionLabelEn = "Go A",     conditionsJson = (string?)null, sortOrder = 1, isRecall = false, assignment = Initiator() },
        new { fromStateName = SFork,   toStateName = SChildB, actionName = "GoB",     actionLabelVi = "Tới B",  actionLabelEn = "Go B",     conditionsJson = (string?)null, sortOrder = 2, isRecall = false, assignment = Initiator() },
        new { fromStateName = SChildA, toStateName = SJoin,   actionName = "FinishA", actionLabelVi = "Xong A", actionLabelEn = "Finish A", conditionsJson = (string?)null, sortOrder = 3, isRecall = false, assignment = Initiator() },
        new { fromStateName = SChildB, toStateName = SJoin,   actionName = "FinishB", actionLabelVi = "Xong B", actionLabelEn = "Finish B", conditionsJson = (string?)null, sortOrder = 4, isRecall = false, assignment = Initiator() },
    ];

    private static object BodyNoBranch() => new { states = States(), transitions = Transitions() };

    private static object BodyWithBranch(
        int branchType = 0, int? requiredCount = null, double? positionX = null, double? positionY = null) => new
        {
            states = States(),
            transitions = Transitions(),
            branches = new object[]
            {
                new
                {
                    branchingStateName = SFork,
                    continuationStateName = SJoin,
                    branchType,
                    requiredCount,
                    allowCancelOnAnyReject = false,
                    timeoutMinutes = 0,
                    positionX,
                    positionY,
                    children = new object[]
                    {
                        new
                        {
                            targetStateName = SChildA, name = "task-a", displayNameVi = "Việc A",
                            departmentId = (string?)null, defaultAssigneeId = (Guid?)null, sortOrder = 1,
                            resolutions = new object[] { new { resolutionType = 0, resultingStateName = SJoin } },
                        },
                        new
                        {
                            targetStateName = SChildB, name = "task-b", displayNameVi = "Việc B",
                            departmentId = (string?)null, defaultAssigneeId = (Guid?)null, sortOrder = 2,
                            resolutions = Array.Empty<object>(),
                        },
                    },
                },
            },
        };

    private async Task<Guid> SeedEmptyDefinitionAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(
            gen: gen,
            name: $"branch-authoring integ {Guid.NewGuid():N}",
            description: "Empty-graph definition for branch authoring integration test",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"br-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.PublicId;
    }
}
