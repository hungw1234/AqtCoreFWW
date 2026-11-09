using AqtCoreFW.BusinessProcess.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetBpRunSubmissions;

/// <summary>
/// Resolve the latest submitted form data for a BP-Run so the run detail page can show what was
/// already entered (intake + each state form). Read-only; one row per distinct form (most recent
/// submission). TenantId from JWT for the scope guard.
/// </summary>
public sealed record GetBpRunSubmissionsQuery(
    Guid WorkflowInstanceId,
    Guid TenantId)
    : IRequest<Result<IReadOnlyList<BpRunSubmissionDto>>>;
