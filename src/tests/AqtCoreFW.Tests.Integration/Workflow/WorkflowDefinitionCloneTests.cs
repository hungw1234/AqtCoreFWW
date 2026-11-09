using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static AqtCoreFW.Tests.Integration.Workflow.ParallelBranchTestSeed;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Clone-to-new-version deep-copy fidelity. Guards the regression where cloning copied only the
/// WorkflowDefinition aggregate (states + transitions) and silently dropped the two satellite families
/// that live outside it — transition assignments (separate entity keyed by transition id) and parallel
/// branches (separate aggregate keyed by definition id) — producing an un-saveable, un-activatable clone.
///
/// Both facts drive the real <c>POST /definitions/{id}/clone</c> endpoint, then assert the persisted
/// clone graph + prove the clone activates (the bug made activation impossible). The parallel fact covers
/// fork + child pools; the linear fact proves the assignment-copy fix is general (not parallel-specific).
/// </summary>
[Collection("Integration")]
public sealed class WorkflowDefinitionCloneTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task CloneParallelDefinition_DeepCopiesForkChildPoolsAndTransitionAssignments_AndIsActivatable()
    {
        // Seed with one branch child carrying a resolution that routes to a state, so the clone exercises
        // the resolution sub-copy + its ResultingStateName→Id re-point (the default seed uses no resolutions).
        var seeded = await SeedActivatedParallelDefinitionAsync(
            Factory, TransitionsPayload(withAssignment: true), BranchesWithResolutionPayload());
        var admin = seeded.AdminClient;

        var clonePublicId = await CloneAsync(admin, seeded.DefinitionPublicId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

            var clone = await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == clonePublicId);
            clone.Version.Should().Be(2, "clone is the next version");
            clone.IsLatest.Should().BeTrue();
            clone.IsActive.Should().BeFalse("a fresh clone is a Draft");

            var source = await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == seeded.DefinitionPublicId);
            source.IsLatest.Should().BeFalse("cloning supersedes the source as latest");

            // Transition assignments — the family the clone used to drop (3 pooled transitions).
            var cloneTransitionIds = await ctx.Set<WorkflowTransition>().AsNoTracking()
                .Where(t => t.DefinitionId == clone.Id).Select(t => t.Id).ToListAsync();
            cloneTransitionIds.Should().HaveCount(3);

            var assignments = await ctx.Set<WorkflowTransitionAssignment>().AsNoTracking()
                .Include(a => a.Clauses)
                .Where(a => cloneTransitionIds.Contains(a.WorkflowTransitionId))
                .ToListAsync();
            assignments.Should().HaveCount(3, "every cloned transition must keep its pool rule");
            assignments.Should().OnlyContain(a => a.Clauses.Any(c => c.Mode == AssignmentClauseMode.ByPermission),
                "the seeded pool is a ByPermission clause");

            // Parallel branches — the second dropped family (1 fork, 2 children, each pooled).
            var branches = await ctx.Set<WorkflowParallelBranch>().AsNoTracking()
                .Include(b => b.Children).ThenInclude(c => c.Assignment!).ThenInclude(a => a.Clauses)
                .Include(b => b.Children).ThenInclude(c => c.Resolutions)
                .Where(b => b.DefinitionId == clone.Id)
                .ToListAsync();
            branches.Should().HaveCount(1, "the AND fork must be deep-copied");
            var branch = branches[0];
            branch.BranchType.Should().Be(ParallelBranchType.And);
            branch.Children.Should().HaveCount(2);
            branch.Children.Should().OnlyContain(c => c.Assignment != null && c.Assignment.Clauses.Count > 0,
                "each branch child must keep its claimable pool");

            // Branch-child resolution survives the clone with its resulting-state re-pointed to the CLONE's
            // own state (the ResultingStateName→Id round-trip in the branch projection).
            var cloneStateIdByName = await ctx.Set<WorkflowState>().AsNoTracking()
                .Where(s => s.DefinitionId == clone.Id).ToDictionaryAsync(s => s.Name, s => s.Id);
            var reviewA = branch.Children.First(c => c.Name == "review-a");
            reviewA.Resolutions.Should().ContainSingle("the seeded child carries one resolution");
            reviewA.Resolutions[0].ResolutionType.Should().Be(BranchResolutionType.Rejected);
            reviewA.Resolutions[0].ResultingStateId.Should().Be(cloneStateIdByName["Closed"],
                "the resolution's resulting state must re-point to the clone's own state, not the source's");
        }

        // The decisive proof the graph is valid: supersede v1, then the clone activates clean.
        // Before the fix this returned a graph-validation failure (5 violations).
        (await admin.PostAsync($"{DefinitionsUrl}/{seeded.DefinitionPublicId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var activate = await admin.PostAsync($"{DefinitionsUrl}/{clonePublicId}/activate", null);
        var activateRaw = await activate.Content.ReadAsStringAsync();
        activate.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"a faithfully-cloned valid graph must activate. Body: {activateRaw}");
    }

    [Fact]
    public async Task CloneLinearPooledDefinition_PreservesTransitionAssignments_AndIsActivatable()
    {
        var (admin, sourcePublicId) = await SeedActivatedLinearPooledDefinitionAsync();

        var clonePublicId = await CloneAsync(admin, sourcePublicId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

            var clone = await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == clonePublicId);
            var cloneTransitionIds = await ctx.Set<WorkflowTransition>().AsNoTracking()
                .Where(t => t.DefinitionId == clone.Id).Select(t => t.Id).ToListAsync();
            cloneTransitionIds.Should().HaveCount(2);

            var assignments = await ctx.Set<WorkflowTransitionAssignment>().AsNoTracking()
                .Include(a => a.Clauses)
                .Where(a => cloneTransitionIds.Contains(a.WorkflowTransitionId))
                .ToListAsync();
            assignments.Should().HaveCount(2,
                "a purely linear pooled def loses no assignments on clone — the fix is not parallel-specific");

            var branches = await ctx.Set<WorkflowParallelBranch>().AsNoTracking()
                .Where(b => b.DefinitionId == clone.Id).ToListAsync();
            branches.Should().BeEmpty("a linear def clones with no branches");
        }

        (await admin.PostAsync($"{DefinitionsUrl}/{sourcePublicId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var activate = await admin.PostAsync($"{DefinitionsUrl}/{clonePublicId}/activate", null);
        var activateRaw = await activate.Content.ReadAsStringAsync();
        activate.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"the cloned linear pooled graph must activate. Body: {activateRaw}");
    }

    [Fact]
    public async Task CloneDefinition_RemapsAutoTransitionOnTimeout_ToClonesOwnTransition()
    {
        var (admin, sourcePublicId) = await SeedActivatedLinearPooledDefinitionAsync();

        // The auto-timeout pointer references a transition id; save-graph is replace-all so it cannot be
        // set through the wire without dangling. Patch the source's "Submitted" state directly to point at
        // the source's real "Close" transition — a valid precondition the engine supports but HTTP can't reach.
        long sourceCloseTransitionId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var src = await ctx.WorkflowDefinitions.FirstAsync(d => d.PublicId == sourcePublicId);
            var transitions = await ctx.Set<WorkflowTransition>().Where(t => t.DefinitionId == src.Id).ToListAsync();
            var states = await ctx.Set<WorkflowState>().Where(s => s.DefinitionId == src.Id).ToListAsync();
            sourceCloseTransitionId = transitions.First(t => t.ActionName == "Close").Id;
            var submitted = states.First(s => s.Name == "Submitted");
            submitted.Update(
                submitted.Name, submitted.DisplayNameVi, submitted.DisplayNameEn,
                submitted.IsInitial, submitted.IsFinal, submitted.Color, submitted.SortOrder,
                submitted.SlaHours, sourceCloseTransitionId, submitted.PositionX, submitted.PositionY);
            await ctx.SaveChangesAsync();
        }

        var clonePublicId = await CloneAsync(admin, sourcePublicId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var clone = await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == clonePublicId);
            var cloneTransitions = await ctx.Set<WorkflowTransition>().AsNoTracking()
                .Where(t => t.DefinitionId == clone.Id).ToListAsync();
            var cloneSubmitted = await ctx.Set<WorkflowState>().AsNoTracking()
                .FirstAsync(s => s.DefinitionId == clone.Id && s.Name == "Submitted");
            var cloneCloseTransitionId = cloneTransitions.First(t => t.ActionName == "Close").Id;

            cloneSubmitted.AutoTransitionOnTimeoutId.Should().NotBeNull("the clone must keep the auto-timeout wiring");
            cloneSubmitted.AutoTransitionOnTimeoutId.Should().Be(cloneCloseTransitionId,
                "the pointer must be remapped to the CLONE's transition, resolved via the stable (FromStateName, ActionName) key");
            cloneSubmitted.AutoTransitionOnTimeoutId.Should().NotBe(sourceCloseTransitionId,
                "a clone that kept the source's transition id would dangle (the bug the remap prevents)");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Mirror of the shared parallel branch payload, but child "review-a" carries one resolution
    /// (Rejected → Closed) so the clone exercises the resolution sub-copy + ResultingStateName re-point.</summary>
    private static object[] BranchesWithResolutionPayload()
    {
        object Pool() => new
        {
            CombineMode = AssignmentCombineMode.Union,
            AllowAssignorOverride = false,
            Clauses = new[] { new { Mode = AssignmentClauseMode.ByPermission, RefsJson = $"[\"{PoolPerm}\"]" } },
        };
        object Child(string target, string name, string vi, int sort, object[] resolutions) => new
        {
            TargetStateName = target,
            Name = name,
            DisplayNameVi = vi,
            SortOrder = sort,
            Resolutions = resolutions,
            DepartmentId = (string?)null,
            DefaultAssigneeId = (Guid?)null,
            Assignment = Pool(),
        };
        return
        [
            new
            {
                BranchingStateName = "Submitted",
                ContinuationStateName = "Returned",
                BranchType = ParallelBranchType.And,
                AllowCancelOnAnyReject = false,
                TimeoutMinutes = 0,
                Children = new[]
                {
                    Child("UnderReview", "review-a", "Duyệt A", 1,
                        [new { ResolutionType = BranchResolutionType.Rejected, ResultingStateName = "Closed" }]),
                    Child("Assigned", "review-b", "Duyệt B", 2, Array.Empty<object>()),
                },
            },
        ];
    }

    private static async Task<Guid> CloneAsync(HttpClient admin, Guid sourcePublicId)
    {
        var resp = await admin.PostAsync($"{DefinitionsUrl}/{sourcePublicId}/clone", null);
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"clone must succeed. Body: {raw}");
        return JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("data").GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Seeds + activates a minimal LINEAR pooled definition (Draft → Submitted → Closed, both transitions
    /// ByPermission-pooled, no branches) through the real authoring endpoints. Isolates the
    /// transition-assignment-copy fix from the fork copy.
    /// </summary>
    private async Task<(HttpClient Admin, Guid PublicId)> SeedActivatedLinearPooledDefinitionAsync()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var admin = Factory.CreateAuthenticatedClient(
            userId: userId.ToString(), roles: ["SystemAdmin"], tenantId: tenantId.ToString());

        object[] states =
        [
            new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft", IsInitial = true, IsFinal = false, Color = "#9E9E9E", SortOrder = 1 },
            new { Name = "Submitted", DisplayNameVi = "Đã nộp", DisplayNameEn = "Submitted", IsInitial = false, IsFinal = false, Color = "#FAAD14", SortOrder = 2 },
            new { Name = "Closed", DisplayNameVi = "Đóng", DisplayNameEn = "Closed", IsInitial = false, IsFinal = true, Color = "#52C41A", SortOrder = 3 },
        ];

        object Pool() => new
        {
            CombineMode = AssignmentCombineMode.Union,
            AllowAssignorOverride = false,
            Clauses = new[] { new { Mode = AssignmentClauseMode.ByPermission, RefsJson = $"[\"{PoolPerm}\"]" } },
        };
        object[] transitions =
        [
            new { FromStateName = "Draft", ToStateName = "Submitted", ActionName = "Submit", ActionLabelVi = "Nộp", ActionLabelEn = "Submit", ConditionsJson = (string?)null, SortOrder = 1, Assignment = Pool() },
            new { FromStateName = "Submitted", ToStateName = "Closed", ActionName = "Close", ActionLabelVi = "Đóng", ActionLabelEn = "Close", ConditionsJson = (string?)null, SortOrder = 2, Assignment = Pool() },
        ];

        var createResp = await admin.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Linear Pooled Clone Test",
            Description = "Linear pooled def for clone assignment-copy coverage",
            TenantId = tenantId.ToString(),
            CreatedBy = userId,
            EntityType = "Generic",
            States = states,
            Transitions = transitions,
        });
        var createRaw = await createResp.Content.ReadAsStringAsync();
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, $"create must succeed. Body: {createRaw}");
        var publicId = JsonSerializer.Deserialize<JsonElement>(createRaw)
            .GetProperty("data").GetProperty("id").GetGuid();

        var graphResp = await admin.PutAsJsonAsync($"{DefinitionsUrl}/{publicId}/graph", new
        {
            States = states,
            Transitions = transitions,
            Branches = Array.Empty<object>(),
        });
        var graphRaw = await graphResp.Content.ReadAsStringAsync();
        graphResp.StatusCode.Should().Be(HttpStatusCode.OK, $"save-graph must succeed. Body: {graphRaw}");

        var activateResp = await admin.PostAsync($"{DefinitionsUrl}/{publicId}/activate", null);
        var activateRaw = await activateResp.Content.ReadAsStringAsync();
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent, $"activation must succeed. Body: {activateRaw}");

        return (admin, publicId);
    }
}
