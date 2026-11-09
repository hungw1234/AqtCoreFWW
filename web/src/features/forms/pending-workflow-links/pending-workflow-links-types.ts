// pending-workflow-links-types.ts — FE mirror of the BE Phase-05 DTO.
//
// Mirrors AqtCoreFW.Forms.Application.DTOs.PendingWorkflowLinkListItemDto FIELD-FOR-FIELD.
// Any rename/add/remove on the BE side is guarded by PendingWorkflowLinkListItemDtoTests
// (exact-field-set assertion) — keep this interface in lockstep.

/** Lifecycle status of an orphan FormSubmission→Workflow link attempt (BE enum, serialized as string). */
export type WorkflowLinkPendingStatus =
  | 'Pending'
  | 'Retrying'
  | 'Succeeded'
  | 'Shelved';

export interface PendingWorkflowLinkListItemDto {
  /** SubmissionId (Guid AK) — external addressing for the originating FormSubmission. */
  id: string;
  /** FormTemplate.PublicId (Guid T2 AK), retiered from the internal long FK in the handler. */
  formTemplateId: string;
  /** Human display name (VI preferred, falls back to Name) — JOINed in the BE handler. */
  templateName: string;
  approvalWorkflowDefinitionId: string;
  attemptCount: number;
  lastFailureCode: string;
  lastError: string | null;
  nextRetryAt: string | null;
  status: WorkflowLinkPendingStatus;
  resolvedAt: string | null;
  resolvedBy: string | null;
  /** Row creation timestamp (BE Entity.CreatedAt — plain UTC DateTime). */
  createdAt: string;
}
