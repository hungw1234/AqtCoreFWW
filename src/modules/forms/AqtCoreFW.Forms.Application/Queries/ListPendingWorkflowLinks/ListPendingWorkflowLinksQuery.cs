using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Forms.Application.Queries.ListPendingWorkflowLinks;

/// <summary>
/// Lists <c>FormSubmissionWorkflowLinkPendingAttempt</c> rows for the admin "Pending Workflow Links"
/// page (P6-DEFER-1 phase 05). Tenant-scoped; optional Status filter
/// (Pending|Retrying|Succeeded|Shelved — matches <see cref="Domain.Enums.WorkflowLinkPendingStatus"/>
/// string names). Gated by <c>PermCodes.Forms.WorkflowLink.Manage</c> at the controller.
/// </summary>
public sealed record ListPendingWorkflowLinksQuery(
    Guid TenantId,
    string? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<PendingWorkflowLinkListItemDto>>;
