using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ClaimTask;

/// <summary>
/// Pool-eligible member claims a pool task (RT2-5).
///
/// Race protection is dual-layer:
///   1. Sequential guard: task.Claim() throws InvalidOperationException when AssigneeId != null
///      (domain invariant). Caught before SaveChanges → translated to AlreadyClaimed (409).
///   2. Concurrent race: RowVersion mismatch when two callers pass guard simultaneously →
///      DbUpdateConcurrencyException at SaveChanges → same AlreadyClaimed (409).
///      Caught in IWorkflowTaskRepository.UpdateWithClaimGuardAsync (Infrastructure boundary)
///      so no EF Core type crosses to Application layer.
///      Loser's SaveChanges fails atomically (audit row tracked in same EF context scope),
///      so no partial-write of audit ledger survives for the loser.
///
/// Pool-eligibility re-validated inside handler (Q2 user-ratified decision):
///   identical predicate to inbox query — PrincipalType=Permission ∩ caller's codes.
///   Returns ForbiddenError (403) if caller not in pool (passes coarse policy gate but
///   fails handler-level pool check — F6 anti-tautology guard).
///
/// UoW: assignmentRepository.AddAsync (tracks, no save) + repository.UpdateWithClaimGuardAsync
///   (flushes both task + audit row in one SaveChangesAsync call, catches RowVersion race).
/// P4 Slice 4-B.
/// </summary>
public sealed class ClaimTaskCommandHandler(
    IWorkflowTaskRepository repository,
    ITaskAssignmentRepository assignmentRepository,
    IWorkflowInstanceRepository instanceRepository,
    IIdentityModuleClient identityClient,
    IOrgUnitModuleClient orgUnitClient)
    : IRequestHandler<ClaimTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        ClaimTaskCommand request,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped fetch — NotFoundError (404) if missing or cross-tenant.
        var fetchResult = await repository.GetByIdAndTenantAsync(
            request.TaskId, request.TenantId, cancellationToken);
        if (fetchResult.IsFailed)
            return Result.Fail(fetchResult.Errors);

        var task = fetchResult.Value;

        // Pool-eligibility check (Q2): re-validate before domain Claim() to produce a clean 403
        // rather than leaking "task already claimed" information to non-pool callers.
        // Uses IIdentityModuleClient — Permission codes are 10-min cached (acceptable staleness),
        // Role codes + group codes are direct-DB (LIVE) per T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION
        // + EXT-GROUP (direct-only semantic; group-inherited roles intentionally excluded).
        // OrgUnit codes are direct-DB (LIVE) per RT2-15 Phase 04 (temporal assignment at claim time).
        var permCodes = await identityClient.GetEffectivePermissionCodesAsync(
            request.ActorId, cancellationToken);
        var roleCodes = await identityClient.GetUserRoleCodesAsync(
            request.ActorId, request.TenantId, cancellationToken);
        var groupCodes = await identityClient.GetUserGroupCodesAsync(
            request.ActorId, request.TenantId, cancellationToken);
        var orgUnitCodes = await orgUnitClient.GetUserOrgUnitCodesAsync(
            request.ActorId, request.TenantId, cancellationToken);

        var isEligible = await repository.IsPoolEligibleAsync(
            request.TaskId, permCodes, roleCodes, groupCodes, orgUnitCodes, request.TenantId, cancellationToken);

        if (!isEligible)
            return Result.Fail(new ForbiddenError(
                $"User '{request.ActorId}' is not pool-eligible for task '{request.TaskId}'."));

        // Sequential race guard: Claim() throws InvalidOperationException when already claimed.
        try
        {
            task.Claim(request.ActorId, request.ActorId);
        }
        catch (InvalidOperationException)
        {
            // Domain guard fired — task was claimed between our fetch and this line.
            return Result.Fail(WorkflowErrors.AlreadyClaimed(request.TaskId));
        }

        // Track audit row — no save yet (TaskAssignmentRepository.AddAsync is track-only).
        var assignment = TaskAssignment.Create(
            task.Id,
            request.ActorId,
            request.ActorId,
            TaskAssignmentType.Claim);

        await assignmentRepository.AddAsync(assignment, cancellationToken);

        // Concurrent race guard: UpdateWithClaimGuardAsync catches DbUpdateConcurrencyException
        // (RowVersion mismatch) at the Infrastructure boundary, returning AlreadyClaimed.
        // Flushes both task mutation and audit row atomically.
        var updateResult = await repository.UpdateWithClaimGuardAsync(task, cancellationToken);
        if (updateResult.IsFailed)
            return Result.Fail(updateResult.Errors);

        // Resolve instance public id for DTO (same pattern as AssignTask/ReassignTask).
        var instancePublicId = await instanceRepository.GetPublicIdByInternalIdAsync(
            task.WorkflowInstanceId, cancellationToken);
        if (instancePublicId is null)
            return Result.Fail(new NotFoundError(
                $"WorkflowInstance internal Id '{task.WorkflowInstanceId}' missing — orphan FK invariant violation."));

        return Result.Ok(CreateWorkflowTaskCommandHandler.MapToDto(task, instancePublicId.Value));
    }
}
