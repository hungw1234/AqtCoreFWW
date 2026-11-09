using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.Commands.ToggleDefinitionActive;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Api;

/// <summary>
/// Seeds a runnable AND-fork parallel-branch workflow definition in the dev tenant — the original
/// "seed 1 parallel sample" goal, now that the runtime engine + per-child assignment subsystem exist.
///
/// Unlike <see cref="SeedWorkflowAsync"/> (which builds a non-parallel def via direct domain calls),
/// this seeds through the same mediator path the visual designer uses — <see cref="SaveDefinitionGraphCommand"/>
/// (handles the branch + per-child assignment tables) then <see cref="ToggleDefinitionActiveCommand"/>
/// (branch-aware validator). Hand-rolling the branch-child-assignment cascade would duplicate non-trivial
/// replace-all logic; the obsolete CreateParallelBranchCommand path it once mirrored is dead.
///
/// No HttpContext is needed: TenantId + ModifiedBy are passed explicitly, and the mediator pipeline
/// resolves BackgroundJobTenantContext (IsSystemAdmin) for the no-request scope.
///
/// Graph (canonical "Generic" state vocab — non-canonical names are rejected at save):
///   Draft → Submitted [fork] ⇒ {UnderReview, Assigned} ⇒ Returned [join, AND] → Closed [final]
/// Both children + the Submit/Close transitions pool on ByPermission WORKFLOW.TASK.PROCESS, which the
/// seeded GovOfficer holds (E2ETestDataSeeder.RolePermissions.cs grants WORKFLOW.TASK.*), so officer@dev.local
/// can claim and resolve the forked children end-to-end.
/// </summary>
public sealed partial class E2ETestDataSeeder
{
    // Pinned PublicId so browser/E2E runs can reference the demo def directly.
    public static readonly Guid ParallelWorkflowDefId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    // GovOfficer holds WORKFLOW.TASK.* — the pool the officer claims through.
    private const string ParallelPoolPerm = "WORKFLOW.TASK.PROCESS";

    // Non-empty seeder actor: the mediator commands validate ModifiedBy != empty (SystemActorId is all-zeros,
    // which the command validator rejects). It is an audit stamp only (no user FK), so a fixed id is fine and
    // works identically in prod startup and the integration harness (which does not seed users).
    private static readonly Guid SeedActorId = Guid.Parse("5EED0000-0000-0000-0000-000000000001");

    // public static so the integration suite can drive it against a no-HttpContext DI scope (exercising
    // the real mediator save-graph→activate path); mirrors the public BuildPermissionList() test seam.
    public static async Task SeedParallelWorkflowAsync(IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        var db = sp.GetRequiredService<WorkflowDbContext>();

        // Idempotent + self-repairing: skip only when fully seeded (exists AND active). A def that exists
        // but is inactive means a prior run committed the shell then failed at save-graph/activate (each is a
        // separate commit — no spanning tx) — fall through to re-run those idempotent steps so a half-seeded
        // demo def is completed rather than stranded inactive (which would defeat the runnable-demo goal).
        var existing = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.PublicId == ParallelWorkflowDefId, ct);
        if (existing is { IsActive: true })
            return;

        // 1. Create the empty definition shell on first run only (SaveDefinitionGraph is replace-all and
        //    requires the definition to already exist). On a repair run the shell is already present.
        if (existing is null)
        {
            var def = WorkflowDefinition.Create(
                gen: new PinnedGuidGenerator(ParallelWorkflowDefId),
                name: "Demo Parallel Process",
                description: "Runnable AND-fork demo (officer pool) for the parallel-branch runtime",
                tenantId: DevTenantId,
                createdBy: SeedActorId);
            await db.WorkflowDefinitions.AddAsync(def, ct);
            await db.SaveChangesAsync(ct);
        }

        var sender = sp.GetRequiredService<ISender>();

        // One Union/ByPermission clause reused for both transitions and both branch children.
        var poolAssignment = new AssignmentInput(
            AssignmentCombineMode.Union,
            AllowAssignorOverride: false,
            Clauses: [new AssignmentClauseInput(AssignmentClauseMode.ByPermission, $"[\"{ParallelPoolPerm}\"]")]);

        var saveResult = await sender.Send(new SaveDefinitionGraphCommand(
            DefinitionId: ParallelWorkflowDefId,
            TenantId: DevTenantId,
            ModifiedBy: SeedActorId,
            States:
            [
                new GraphStateInput("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#9E9E9E", 1, PositionX: 80, PositionY: 200),
                new GraphStateInput("Submitted", "Đã nộp", "Submitted", false, false, "#FAAD14", 2, PositionX: 300, PositionY: 200),
                new GraphStateInput("UnderReview", "Thẩm định A", "Review A", false, false, "#2196F3", 3, PositionX: 540, PositionY: 90),
                new GraphStateInput("Assigned", "Thẩm định B", "Review B", false, false, "#2196F3", 4, PositionX: 540, PositionY: 310),
                new GraphStateInput("Returned", "Tổng hợp", "Synthesis", false, false, "#1890FF", 5, PositionX: 780, PositionY: 200),
                new GraphStateInput("Closed", "Đóng", "Closed", false, IsFinal: true, "#52C41A", 6, PositionX: 1000, PositionY: 200),
            ],
            Transitions:
            [
                new GraphTransitionInput("Draft", "Submitted", "Submit", "Nộp", "Submit", null, 1) { Assignment = poolAssignment },
                // Validation-reachability edge for the continuation state; the join reaches Returned internally.
                new GraphTransitionInput("Submitted", "Returned", "Continue", "Tiếp tục", "Continue", null, 2) { Assignment = poolAssignment },
                new GraphTransitionInput("Returned", "Closed", "Close", "Đóng", "Close", null, 3) { Assignment = poolAssignment },
            ])
        {
            Branches =
            [
                new GraphBranchInput(
                    BranchingStateName: "Submitted",
                    ContinuationStateName: "Returned",
                    BranchType: ParallelBranchType.And,
                    AllowCancelOnAnyReject: false,
                    TimeoutMinutes: 0,
                    Children:
                    [
                        new GraphBranchChildInput("UnderReview", "review-a", "Thẩm định A", 1, []) { Assignment = poolAssignment },
                        new GraphBranchChildInput("Assigned", "review-b", "Thẩm định B", 2, []) { Assignment = poolAssignment },
                    ],
                    PositionX: 410, PositionY: 200),
            ],
        }, ct);

        if (saveResult.IsFailed)
        {
            logger.LogError("Demo parallel workflow save-graph failed: {Errors}",
                string.Join("; ", saveResult.Errors.Select(e => e.Message)));
            return;
        }

        // 2. Activate through the branch-aware validator (P3): R1 exempts branching/child-target states and
        //    RB5 accepts the non-empty ByPermission child pools (the live-claimant check only gates ByRole).
        var activateResult = await sender.Send(new ToggleDefinitionActiveCommand(
            DefinitionId: ParallelWorkflowDefId,
            TenantId: DevTenantId,
            ModifiedBy: SeedActorId,
            IsActive: true), ct);

        if (activateResult.IsFailed)
            logger.LogError("Demo parallel workflow activation failed: {Errors}",
                string.Join("; ", activateResult.Errors.Select(e => e.Message)));
    }
}
