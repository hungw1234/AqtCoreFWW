using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Shared seed helpers for Workflow integration tests.
///
/// Extracted in PHR-V2 Phase 6 closure (S2 — DRY refactor of 22 LOC × 5 callers).
/// Origin: Phase 5b empty-pool hard-block forced every Workflow integration test that
/// hits POST /api/v1/workflow/instances to seed a ByPermission assignment on the
/// initial state's outgoing transition. Without the assignment, instance creation
/// 422s with GOV_WFL_014 NoHandlerResolved.
/// </summary>
internal static class WorkflowSeedHelpers
{
    /// <summary>
    /// Stage a <see cref="WorkflowTransitionAssignment"/> with a single
    /// <see cref="AssignmentClauseMode.ByPermission"/> clause on the named outgoing
    /// transition of <paramref name="definitionPublicId"/>'s initial state.
    /// </summary>
    /// <param name="factory">Test web-app factory exposing the DI container.</param>
    /// <param name="definitionPublicId">Guid PublicId of the seeded WorkflowDefinition.</param>
    /// <param name="permCode">Permission code that resolves the handler pool (e.g. <c>PermCodes.Workflow.Task.Process</c>).</param>
    /// <param name="actionName">Transition <c>ActionName</c> on the initial state's outgoing edge. Default <c>"Approve"</c>.</param>
    /// <param name="combineMode">Clause combine mode. Default <see cref="AssignmentCombineMode.Union"/>.</param>
    public static async Task SeedFirstStateAssignmentByPermissionAsync(
        ApiFactory factory,
        Guid definitionPublicId,
        string permCode,
        string actionName = "Approve",
        AssignmentCombineMode combineMode = AssignmentCombineMode.Union,
        CancellationToken ct = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var def = await ctx.WorkflowDefinitions.AsNoTracking()
            .FirstAsync(d => d.PublicId == definitionPublicId, ct);
        var transition = await ctx.Set<WorkflowTransition>().AsNoTracking()
            .FirstAsync(t => t.DefinitionId == def.Id && t.ActionName == actionName, ct);

        var assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: transition.Id,
            combineMode: combineMode);
        assignment.AddClause(AssignmentClauseMode.ByPermission, $"""["{permCode}"]""");
        ctx.Set<WorkflowTransitionAssignment>().Add(assignment);
        await ctx.SaveChangesAsync(ct);
    }
}
