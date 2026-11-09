using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ResolveBranchChild;

/// <summary>
/// Resolves a single child within an active parallel branch.
/// If the join condition is met after this resolution, the workflow instance
/// automatically transitions to the branch's ContinuationStateId.
/// Phase-4b B4c: InstanceId = WorkflowInstance.PublicId (T2 Guid AK).
///               ChildStatusId = WorkflowBranchChildStatus.Id (T1 long PK).
/// </summary>
public sealed record ResolveBranchChildCommand(
    Guid InstanceId,
    long ChildStatusId,
    BranchResolutionType ResolutionType,
    Guid ResolvedBy,
    string? Comment,
    Guid TenantId)
    : IRequest<Result>;
