using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Shared seeding + drive helpers for parallel-branch runtime integration tests. Extracted from
/// <see cref="WorkflowParallelBranchRuntimeTests"/> so the runtime, concurrency, and negative-path suites
/// build the SAME proven graph through the real authoring endpoints without duplicating the recipe.
///
/// Everything is static and takes the <see cref="ApiFactory"/> (or an already-authenticated client) so a
/// caller doesn't need to live on <c>IntegrationTestBase</c> — the client factories are public on ApiFactory.
///
/// Graph shape (canonical "Generic" vocab):
///   Draft ──Submit──▶ Submitted [branching] ══fork══▶ {UnderReview, Assigned}
///   Submitted ──Continue──▶ Returned [continuation] ── Close ──▶ Closed [final]
/// </summary>
public static class ParallelBranchTestSeed
{
    public const string DefinitionsUrl = "/api/v1/workflow/definitions";
    public const string InstancesUrl = "/api/v1/workflow/instances";
    public const string TasksUrl = "/api/v1/workflow/tasks";

    // Pool permission for every transition + branch child. Holders can submit / claim / resolve / close.
    public const string PoolPerm = PermCodes.Workflow.Task.Process;

    public sealed record SeededParallelDefinition(
        HttpClient AdminClient,
        Guid TenantId,
        Guid DefinitionPublicId,
        long DefinitionInternalId,
        long SubmitTransitionId,
        long ContinueTransitionId,
        long CloseTransitionId);

    /// <summary>A created + save-graphed (but NOT yet activated) parallel def. For callers asserting an
    /// activation outcome (success OR failure) directly, rather than baking activation into the seed.</summary>
    public sealed record SavedParallelDefinition(
        HttpClient AdminClient,
        Guid TenantId,
        Guid DefinitionPublicId);

    /// <summary>
    /// Creates a parallel-branch definition through the real authoring endpoints (create → save-graph →
    /// activate), so activation exercises the branch-aware validator. Returns the admin client + the
    /// resolved transition ids the runtime needs. A positive <paramref name="timeoutMinutes"/> arms the
    /// branch SLA so <see cref="AqtCoreFW.Workflow.Infrastructure.Jobs.BranchTimeoutCheckerJob"/> can act
    /// on it (the runtime test uses 0 = no timeout).
    /// </summary>
    public static Task<SeededParallelDefinition> SeedActivatedParallelDefinitionAsync(
        ApiFactory factory, int timeoutMinutes = 0)
        => SeedAndActivateAsync(
            factory,
            TransitionsPayload(withAssignment: true),
            BranchesPayload(timeoutMinutes: timeoutMinutes));

    /// <summary>
    /// Seed + activate with caller-supplied transition / branch payloads, for negative-path graphs
    /// (empty-refs pools) that still activate cleanly but diverge at runtime. The graph must still pass
    /// activation — callers asserting an activation FAILURE should use
    /// <see cref="SeedSavedParallelDefinitionAsync"/> and drive activation themselves.
    /// </summary>
    public static Task<SeededParallelDefinition> SeedActivatedParallelDefinitionAsync(
        ApiFactory factory, object[] transitions, object[] branches)
        => SeedAndActivateAsync(factory, transitions, branches);

    /// <summary>
    /// Creates the definition shell + saves the full graph (transitions + branches) but does NOT activate.
    /// Lets a caller assert the activation outcome directly — e.g. a child pooled on a role no active user
    /// holds must be REJECTED by the RB5 live-claimant check at activation.
    /// </summary>
    public static async Task<SavedParallelDefinition> SeedSavedParallelDefinitionAsync(
        ApiFactory factory, object[] transitions, object[] branches)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var adminClient = factory.CreateAuthenticatedClient(
            userId: userId.ToString(), roles: ["SystemAdmin"], tenantId: tenantId.ToString());

        // 1. Create the definition shell (states + transitions, no assignments yet).
        var createResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Parallel Branch Runtime Test",
            Description = "Parallel-branch end-to-end seeding",
            TenantId = tenantId.ToString(),
            CreatedBy = userId,
            EntityType = "Generic",
            States = StatesPayload(),
            Transitions = TransitionsPayload(withAssignment: false),
        });
        var createRaw = await createResp.Content.ReadAsStringAsync();
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, $"definition create must succeed. Body: {createRaw}");
        var defPublicId = JsonSerializer.Deserialize<JsonElement>(createRaw)
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Save the full graph (transitions WITH assignment + the parallel branch with child pools).
        var graphResp = await adminClient.PutAsJsonAsync($"{DefinitionsUrl}/{defPublicId}/graph", new
        {
            States = StatesPayload(),
            Transitions = transitions,
            Branches = branches,
        });
        var graphRaw = await graphResp.Content.ReadAsStringAsync();
        graphResp.StatusCode.Should().Be(HttpStatusCode.OK, $"save-graph with branches must succeed. Body: {graphRaw}");

        return new SavedParallelDefinition(adminClient, tenantId, defPublicId);
    }

    private static async Task<SeededParallelDefinition> SeedAndActivateAsync(
        ApiFactory factory, object[] transitions, object[] branches)
    {
        var saved = await SeedSavedParallelDefinitionAsync(factory, transitions, branches);
        var adminClient = saved.AdminClient;
        var tenantId = saved.TenantId;
        var defPublicId = saved.DefinitionPublicId;

        // 3. Activate — runs the branch-aware validator (R1 exemptions + RB5 child-pool + claimant check).
        var activateResp = await adminClient.PostAsync($"{DefinitionsUrl}/{defPublicId}/activate", null);
        var activateRaw = await activateResp.Content.ReadAsStringAsync();
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent, $"activation of a valid parallel def must succeed. Body: {activateRaw}");

        // Resolve persisted ids the runtime needs.
        await using var scope = factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == defPublicId);
        var persisted = await ctx.Set<WorkflowTransition>().AsNoTracking()
            .Where(t => t.DefinitionId == def.Id).ToListAsync();
        var submitId = persisted.First(t => t.ActionName == "Submit").Id;
        var continueId = persisted.First(t => t.ActionName == "Continue").Id;
        var closeId = persisted.First(t => t.ActionName == "Close").Id;

        return new SeededParallelDefinition(adminClient, tenantId, defPublicId, def.Id, submitId, continueId, closeId);
    }

    /// <summary>Seeds + activates a parallel def, creates an instance, fires the Submit fork, and returns
    /// the seeded def + instance + each child's (taskId, childStatusId) pair.</summary>
    public static async Task<(SeededParallelDefinition Seeded, Guid InstancePublicId, List<(long TaskId, long ChildStatusId)> Children)>
        SeedForkedInstanceAsync(ApiFactory factory, int timeoutMinutes = 0)
    {
        var seeded = await SeedActivatedParallelDefinitionAsync(factory, timeoutMinutes);
        var (instancePublicId, children) = await ForkInstanceAsync(factory, seeded);
        return (seeded, instancePublicId, children);
    }

    /// <summary>Creates an instance against an already-seeded def, fires the Submit fork (asserts it
    /// succeeds), and returns the instance + each child's (taskId, childStatusId) ordered by task id (=
    /// branch-child sort order, since the fan-out stages children in sort order under one SaveChanges).</summary>
    public static async Task<(Guid InstancePublicId, List<(long TaskId, long ChildStatusId)> Children)>
        ForkInstanceAsync(ApiFactory factory, SeededParallelDefinition seeded)
    {
        var instancePublicId = await CreateInstanceAsync(seeded.AdminClient, seeded.DefinitionPublicId);

        var submitter = Guid.NewGuid();
        using var client = factory.CreateClientWithPermissions(
            [PoolPerm], userId: submitter.ToString(), tenantId: seeded.TenantId.ToString());
        var resp = await client.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/transitions",
            new { TransitionId = seeded.SubmitTransitionId, Comment = (string?)null });
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"Submit must fire the fork. Body: {raw}");

        await using var scope = factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.AsNoTracking().FirstAsync(i => i.PublicId == instancePublicId);
        var childTasks = await ctx.WorkflowTasks
            .IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instance.Id && t.BranchChildStatusId != null)
            .OrderBy(t => t.Id)
            .ToListAsync();
        var children = childTasks.Select(t => (t.Id, t.BranchChildStatusId!.Value)).ToList();
        return (instancePublicId, children);
    }

    public static async Task<Guid> CreateInstanceAsync(HttpClient adminClient, Guid definitionPublicId)
    {
        var resp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"instance creation must succeed. Body: {raw}");
        return JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("data").GetProperty("id").GetGuid();
    }

    public static async Task ClaimAsync(HttpClient client, long taskId)
    {
        var resp = await client.PostAsync($"{TasksUrl}/{taskId}/claim", null);
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"pool user must claim child task {taskId}. Body: {raw}");
    }

    public static async Task ResolveBranchChildAsync(
        HttpClient client, Guid instancePublicId, long childStatusId, BranchResolutionType resolution)
    {
        var resp = await client.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/branch-children/{childStatusId}/resolve",
            new { ResolutionType = (int)resolution, Comment = (string?)null });
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, $"claimed pool user must resolve child {childStatusId}. Body: {raw}");
    }

    // State names MUST be canonical vocab for entity type "Generic"
    // (Draft, Submitted, UnderReview, Assigned, Approved, Rejected, Returned, ReturnedForRevision,
    // Closed, Cancelled). Roles in this graph: Submitted=branching, UnderReview/Assigned=child targets,
    // Returned=continuation, Closed=final.
    public static object[] StatesPayload() =>
    [
        new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft", IsInitial = true, IsFinal = false, Color = "#9E9E9E", SortOrder = 1 },
        new { Name = "Submitted", DisplayNameVi = "Đã nộp", DisplayNameEn = "Submitted", IsInitial = false, IsFinal = false, Color = "#FAAD14", SortOrder = 2 },
        new { Name = "UnderReview", DisplayNameVi = "Duyệt A", DisplayNameEn = "Review A", IsInitial = false, IsFinal = false, Color = "#2196F3", SortOrder = 3 },
        new { Name = "Assigned", DisplayNameVi = "Duyệt B", DisplayNameEn = "Review B", IsInitial = false, IsFinal = false, Color = "#2196F3", SortOrder = 4 },
        new { Name = "Returned", DisplayNameVi = "Tổng hợp", DisplayNameEn = "Synthesis", IsInitial = false, IsFinal = false, Color = "#1890FF", SortOrder = 5 },
        new { Name = "Closed", DisplayNameVi = "Đóng", DisplayNameEn = "Closed", IsInitial = false, IsFinal = true, Color = "#52C41A", SortOrder = 6 },
    ];

    public static object[] TransitionsPayload(bool withAssignment, bool emptyRefsContinuationOutgoing = false)
    {
        object? Assignment(string refsJson) => withAssignment
            ? new
            {
                CombineMode = AssignmentCombineMode.Union,
                AllowAssignorOverride = false,
                Clauses = new[] { new { Mode = AssignmentClauseMode.ByPermission, RefsJson = refsJson } },
            }
            : null;

        var validRefs = $"[\"{PoolPerm}\"]";
        // The continuation state ("Returned") is fed by its OUTGOING transition ("Close"). An empty refs
        // array there parses to no codes, so the resolver returns an empty continuation pool at join time
        // → the join fails CLOSED. Default (false) keeps a valid claimable pool (byte-identical to before).
        var closeRefs = emptyRefsContinuationOutgoing ? "[]" : validRefs;

        return
        [
            new { FromStateName = "Draft", ToStateName = "Submitted", ActionName = "Submit", ActionLabelVi = "Nộp", ActionLabelEn = "Submit", ConditionsJson = (string?)null, SortOrder = 1, Assignment = Assignment(validRefs) },
            // Validation-reachability edge for the continuation state; never executed at runtime.
            new { FromStateName = "Submitted", ToStateName = "Returned", ActionName = "Continue", ActionLabelVi = "Tiếp tục", ActionLabelEn = "Continue", ConditionsJson = (string?)null, SortOrder = 2, Assignment = Assignment(validRefs) },
            new { FromStateName = "Returned", ToStateName = "Closed", ActionName = "Close", ActionLabelVi = "Đóng", ActionLabelEn = "Close", ConditionsJson = (string?)null, SortOrder = 3, Assignment = Assignment(closeRefs) },
        ];
    }

    /// <summary>Pool variants for a branch child — each passes design-time validation differently but
    /// diverges at runtime, so the negative-path suite can exercise reachable engine behavior.</summary>
    public enum ChildPoolKind
    {
        /// <summary>ByPermission [PoolPerm] — claimable; the happy-path default.</summary>
        ValidPermission,
        /// <summary>No assignment — RB5 rejects this at activation (cannot go live).</summary>
        None,
        /// <summary>ByPermission with an empty refs array — a clause exists (passes RB5) and the array is
        /// valid JSON with no unknown codes (passes activation), but the resolver pushes no predicate so the
        /// pool resolves Empty at runtime → fork-entry fails CLOSED.</summary>
        EmptyRefs,
        /// <summary>ByRole referencing a role code no active user holds — REJECTED at activation by the RB5
        /// live-claimant check (<c>ResolveActiveUsersInRolesAsync</c> returns nobody), since the staged child
        /// task would otherwise be unclaimable and its AND-join could never complete.</summary>
        UnheldRole,
    }

    // A role code intentionally held by no test actor (CreateClientWithPermissions grants permissions, not
    // roles, and no AspNetUserRoles row exists for it), so the RB5 live-claimant check rejects a child
    // pooled on it at activation — closing the previously-reachable AND-join deadlock.
    public const string UnheldRoleCode = "WF_DEADLOCK_UNHELD_ROLE";

    public static object[] BranchesPayload(
        int timeoutMinutes = 0, ChildPoolKind secondChildPool = ChildPoolKind.ValidPermission)
    {
        object PermissionPool(string refsJson) => new
        {
            CombineMode = AssignmentCombineMode.Union,
            AllowAssignorOverride = false,
            Clauses = new[] { new { Mode = AssignmentClauseMode.ByPermission, RefsJson = refsJson } },
        };

        object RolePool(string roleCode) => new
        {
            CombineMode = AssignmentCombineMode.Union,
            AllowAssignorOverride = false,
            Clauses = new[] { new { Mode = AssignmentClauseMode.ByRole, RefsJson = $"[\"{roleCode}\"]" } },
        };

        object? PoolFor(ChildPoolKind kind) => kind switch
        {
            ChildPoolKind.ValidPermission => PermissionPool($"[\"{PoolPerm}\"]"),
            ChildPoolKind.None => null,
            ChildPoolKind.EmptyRefs => PermissionPool("[]"),
            ChildPoolKind.UnheldRole => RolePool(UnheldRoleCode),
            _ => PermissionPool($"[\"{PoolPerm}\"]"),
        };

        object Child(string target, string name, string vi, int sort, ChildPoolKind pool) => new
        {
            TargetStateName = target,
            Name = name,
            DisplayNameVi = vi,
            SortOrder = sort,
            Resolutions = Array.Empty<object>(),
            DepartmentId = (string?)null,
            DefaultAssigneeId = (Guid?)null,
            Assignment = PoolFor(pool),
        };

        return
        [
            new
            {
                BranchingStateName = "Submitted",
                ContinuationStateName = "Returned",
                BranchType = ParallelBranchType.And,
                AllowCancelOnAnyReject = false,
                TimeoutMinutes = timeoutMinutes,
                Children = new[]
                {
                    Child("UnderReview", "review-a", "Duyệt A", 1, ChildPoolKind.ValidPermission),
                    Child("Assigned", "review-b", "Duyệt B", 2, secondChildPool),
                },
            },
        ];
    }
}
