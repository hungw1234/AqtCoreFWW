> **OBSOLETE 2026-05-01** — Cases module removed in PR #71. This runbook no longer applies. Kept for historical reference.

# Runbook — Cases / Workflow Status Drift Check

**Scope:** Daily reconciliation between `cases.Cases.Status` (denormalized column used for list filters) and `workflow.WorkflowInstances.CurrentStateId → workflow.WorkflowStates.Name` (authoritative state machine state).

**Introduced:** Phase-04 of Cases→Workflow delegation refactor (2026-04-17).

---

## Why this check exists

After Phase-03, `Case.Status` is updated **inline** inside the transition handler (`RecordSubmit`, `RecordAssign`, `RecordApprove`, `RecordReject`, `RecordClose`) using the `newStateName` returned by `IWorkflowModuleClient.ExecuteTransitionAsync`. Both writes happen in the same Cases handler but touch **separate DbContexts** (Workflow commits first, then Cases commits).

If the Cases commit fails (DB outage, transient concurrency) after Workflow commits, `WorkflowInstance.CurrentState` moves forward while `Case.Status` stays at the old value. User sees stale status; list filters mis-route.

Residual risk: drift window ≤ 1 day (daily reconciliation).

---

## Detection query

Run against the production replica / read-only DB.

```sql
SELECT
    c.Id                        AS CaseId,
    c.TenantId                  AS TenantId,
    c.CaseNumber                AS CaseNumber,
    c.Status                    AS CaseStatus,
    ws.Name                     AS WorkflowState,
    wi.CurrentStateEnteredAt    AS StateEnteredAt,
    c.UpdatedAtUtc              AS CaseUpdatedAt
FROM cases.Cases c
JOIN workflow.WorkflowInstances wi
  ON wi.EntityType = 'Case'
 AND wi.EntityId   = c.Id
 AND wi.TenantId   = c.TenantId
JOIN workflow.WorkflowStates ws
  ON ws.Id = wi.CurrentStateId
WHERE c.Status   <> ws.Name
  AND c.IsDeleted = 0
  AND wi.IsDeleted = 0;
```

**Expected outcome:** zero rows.

---

## Alert thresholds

| Row count | Severity | Action |
|-----------|----------|--------|
| 0         | OK       | No action |
| 1–9       | LOW      | Log. Manual reconcile next business day. |
| 10–49     | MEDIUM   | Slack `#ops-cases`. Investigate root cause same day. |
| ≥ 50      | HIGH     | Page on-call. Suspect systemic failure (DB outage, feature-flag flip, migration). |

---

## Remediation

### Option A — Manual per-case fix (preferred for small counts)

Identify the authoritative state. In 99% of cases, **`WorkflowState` is correct** because Workflow commits first.

```sql
UPDATE cases.Cases
SET Status = @correctStatus,      -- from workflow.WorkflowStates.Name
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = '<ops-operator-id>'
WHERE Id = @caseId
  AND TenantId = @tenantId;
```

Record the fix in the audit log with reason `manual-drift-reconcile` and a link to this runbook.

### Option B — Bulk fix (HIGH severity only, after approval)

Obtain written approval from engineering lead before running. Dry-run first with `SELECT` form.

```sql
UPDATE c
SET c.Status = ws.Name,
    c.UpdatedAtUtc = SYSUTCDATETIME(),
    c.UpdatedBy = '<ops-bulk-reconcile-id>'
FROM cases.Cases c
JOIN workflow.WorkflowInstances wi
  ON wi.EntityType = 'Case' AND wi.EntityId = c.Id AND wi.TenantId = c.TenantId
JOIN workflow.WorkflowStates ws ON ws.Id = wi.CurrentStateId
WHERE c.Status   <> ws.Name
  AND c.IsDeleted = 0 AND wi.IsDeleted = 0;
```

### Option C — Roll back to legacy path (emergency)

If drift is growing and correlates with a recent release of the delegated path, flip the feature flag off to freeze drift while root-causing:

```
feature:cases.workflow-delegation = false  (per tenant or global)
```

Legacy handler path (`case.Submit/Approve/…` with hard-coded engine) then re-owns state transitions. Note: new-path transitions that already committed stay — this does not rewind history.

---

## Escalation

- **Engineering owner:** Cases module team (see CODEOWNERS)
- **Runbook owner:** Ops / SRE
- **Related runbooks:** Workflow instance reconciliation (TBD), Feature-flag flip procedure (`docs/runbooks/feature-flag-operations.md` if present)

---

## Change log

| Date       | Change                                              |
|------------|-----------------------------------------------------|
| 2026-04-17 | Initial runbook created in Phase-04 deliverables.   |
