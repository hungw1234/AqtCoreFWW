namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>
/// Admin list-item projection of a <c>FormSubmissionWorkflowLinkPendingAttempt</c> row
/// (P6-DEFER-1 phase 05 — "Pending Workflow Links" admin page, consumed by phase 06 FE).
///
/// Id = SubmissionId (Guid AK — external addressing for the originating FormSubmission).
/// The row's own internal long PK is never exposed. FormTemplateId is the entity's internal long FK
/// (<c>FormSubmissionWorkflowLinkPendingAttempt.FormTemplateId</c>) RETIERED here to
/// <c>FormTemplate.PublicId</c> (Guid T2 AK) via a JOIN in the handler — mirrors
/// <c>ListFormSubmissionsQueryHandler</c>'s Phase-07 O-2 precedent so the FE never sees an internal PK.
/// ApprovalWorkflowDefinitionId is already a cross-module Guid AK on the entity (no retiering needed).
///
/// Surfaces all 4 lifecycle statuses (Pending/Retrying/Succeeded/Shelved) so admins can audit
/// dead-letters, not just active rows.
///
/// Dapper requires a class with a parameterless ctor here (WARNING per
/// <c>AuditLogDto.cs</c>'s documented gotcha — a positional record mixing a plain
/// <c>DateTime</c> column (<see cref="CreatedAt"/>, from <c>AuditableEntity</c>/<c>Entity</c>) with
/// <c>DateTimeOffset?</c> columns fails Dapper's constructor-matching materialization at runtime
/// with an opaque "parameterless default constructor... is required" InvalidOperationException).
/// </summary>
public sealed class PendingWorkflowLinkListItemDto
{
    public Guid Id { get; set; }
    public Guid FormTemplateId { get; set; }

    /// <summary>Display name of the originating template (VI preferred, falls back to Name) — JOINed
    /// in the handler so the admin list shows a human label instead of only the Guid FK.</summary>
    public string TemplateName { get; set; } = string.Empty;

    public Guid ApprovalWorkflowDefinitionId { get; set; }
    public int AttemptCount { get; set; }
    public string LastFailureCode { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }

    /// <summary>Row creation timestamp (<c>Entity.CreatedAt</c> convention: <c>DateTime</c>, UTC, datetime2(3) — NOT DateTimeOffset).</summary>
    public DateTime CreatedAt { get; set; }
}
