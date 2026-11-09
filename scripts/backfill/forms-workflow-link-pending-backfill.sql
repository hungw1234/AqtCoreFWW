-- ============================================================================
-- Backfill: arm orphan FormSubmission -> Workflow links for the retry job
-- ----------------------------------------------------------------------------
-- Seeds ONE retry-armed 'Pending' row per historical orphan submission so the
-- Hangfire retry job (FormWorkflowLinkRetryJob) claims and resolves it.
--
-- An "orphan" = a submission whose template HAS an approval workflow, but which
-- never got a WorkflowInstanceId linked (instance-creation failed at submit time
-- before the retry job existed). Window start = 2026-05-21 (the 5b fail-closed
-- graceful-skip ship date; earlier submissions predate the orphan-producing path).
--
-- IDEMPOTENT: the WHERE NOT EXISTS keys on the unique index
-- UX_FormSubmissionWfLinkPendingAttempts_Submission_Tenant (SubmissionId, TenantId)
-- so re-running inserts nothing for rows already present.
--
-- COLUMN LIST IS EXPLICIT (never SELECT *): the column set + order must match the
-- entity's insert shape from migration 20260522144724. Every NOT-NULL column is
-- listed; nullable columns (LastError, ResolvedAt, ResolvedBy, UpdatedAt,
-- ModifiedBy) are omitted and default to NULL. NextRetryAt is armed to now so the
-- row lands in the filtered claim index and the job picks it up next tick.
--
-- PRINCIPAL: this TARGET table has no DB-level RLS policy (app-layer EF query filter only), so the
-- INSERT itself is never RLS-blocked. What DOES need a privileged principal is the cross-tenant SOURCE
-- SELECT: forms.FormSubmissions IS RLS-protected, so an app principal would only see its own tenant's
-- rows. Run as the migration / db_owner principal (one-shot ops action) to read every tenant's orphans
-- in one pass, OR loop per tenant with sp_set_session_context. See the runbook.
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

INSERT INTO forms.FormSubmissionWorkflowLinkPendingAttempts
    (TenantId,
     SubmissionId,
     FormTemplateId,
     ApprovalWorkflowDefinitionId,
     SubmittedBy,
     AttemptCount,
     LastFailureCode,
     Status,
     NextRetryAt,
     CreatedAt,
     IsDeleted,
     CreatedBy,
     ClassificationLevel)
SELECT
     s.TenantId,
     s.PublicId,                       -- SubmissionId AK = FormSubmission.PublicId
     s.FormTemplateId,                 -- internal long FK (entity stores the long, not the Guid)
     t.ApprovalWorkflowDefinitionId,   -- cross-module Guid AK
     s.SubmittedBy,
     0,                                -- AttemptCount: fresh
     'BACKFILL_ORPHAN',                -- distinguishes backfilled rows in ops queries
     'Pending',                        -- Status string (HasConversion<string>)
     SYSDATETIMEOFFSET(),              -- NextRetryAt armed = now (datetimeoffset, UTC on a UTC-clock server) -> job claims next tick
     SYSUTCDATETIME(),                 -- CreatedAt (datetime2(3) UTC convention)
     0,                                -- IsDeleted
     0x00000000000000000000000000000000, -- CreatedBy = Guid.Empty (system-attributed backfill)
     1                                 -- ClassificationLevel = Internal (entity default)
FROM forms.FormSubmissions s
INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
WHERE t.ApprovalWorkflowDefinitionId IS NOT NULL
  AND s.WorkflowInstanceId IS NULL
  AND s.IsDeleted = 0
  AND s.SubmittedAt >= '2026-05-21'
  AND NOT EXISTS (
      SELECT 1
      FROM forms.FormSubmissionWorkflowLinkPendingAttempts a
      WHERE a.SubmissionId = s.PublicId
        AND a.TenantId = s.TenantId);

PRINT CONCAT('Backfill inserted rows: ', @@ROWCOUNT);

COMMIT TRANSACTION;
