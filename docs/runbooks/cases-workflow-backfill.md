# Runbook — Cases → Workflow Backfill (Phase-05)

> **OBSOLETE (2026-05-01):** Cases module removed in plan `260501-1152-full-delete-cases-module`. This runbook is archived for historical reference only. Do NOT run against post-v2.157 environments (schema `cases` dropped). Run `infra/scripts/drop-cases-schema.sql` instead for cleanup.

**Scope:** One-shot data migration that creates a `workflow.WorkflowInstances` row for every existing `cases.Cases` row. Required before the feature flag `feature:cases.workflow-delegation` can be flipped ON in production; otherwise transition handlers will 404 (`WorkflowInstanceNotFound → GOV_WFL_*`).

**Introduced:** Phase-05 of Cases→Workflow delegation refactor (2026-04-17).

**Owner:** Cases module team + Ops / SRE for execution.

**Sequencing:** Run AFTER Phase-00/01 deployed (taxonomy + CaseProcess definition seeded) and AFTER Phase-02/03/04 code is in the release artifact, but BEFORE the feature flag is flipped ON.

---

## Artifacts

All under `src/host/AqtCoreFW.Api/Scripts/`:

| Script | Purpose | Mode |
|--------|---------|------|
| `backfill-case-workflow-instances-dryrun.sql` | 4 validation queries — pre-flight gate | READ-ONLY |
| `backfill-case-workflow-instances.sql` | Batched backfill (1000 rows/TX) + inline parity verify | WRITES — target env only |
| `rollback-case-workflow-instances.sql` | Delete all Case-derived instances + history | DESTRUCTIVE |

---

## Pre-flight (BLOCKING)

Run the dry-run script and inspect each query result in order. **All four must pass** before running the main backfill.

```bash
sqlcmd -S <server> -d <db> -i src/host/AqtCoreFW.Api/Scripts/backfill-case-workflow-instances-dryrun.sql
```

| Query | Expected | STOP condition |
|-------|----------|----------------|
| **Q1** — CaseProcess definition | exactly 1 row, `IsActive=1`, `IsLatest=1`, `DefinitionTenantId=00000000-0000-0000-0000-000000000000`, `StateCount=7` | 0 rows, or mismatch on any field → Phase-01 seeder not applied / env skew → investigate before backfill |
| **Q2** — Status orphans | **0 rows** | any row returned → a `Case.Status` value exists with no matching `WorkflowState.Name`. DO NOT run backfill. Root cause: Phase-01 seeder not re-run after a `CaseStatus` enum change, OR corrupted Case row. Fix + re-dry-run. |
| **Q3** — Workload estimate | `CaseCount = ExistingInstances + MissingInstances` arithmetic holds | mismatch → data-integrity bug, investigate stray instances (perhaps another Entity has `EntityType='Case'`) |
| **Q4** — Distribution | status histogram for awareness | N/A (non-blocking) |

**Expected runtime for pre-flight: < 10s** on prod-sized dataset.

---

## Main backfill

**Timing guidance:** runnable during business hours — per-batch transactions (1000 rows), no global lock on `cases.Cases`. Concurrent Case writes proceed normally; backfill only inserts where `NOT EXISTS` in `workflow.WorkflowInstances`.

**Precondition:** Feature flag `feature:cases.workflow-delegation` is OFF (default). Writes to Cases continue via the legacy path and do NOT create WorkflowInstance rows, so the backfill window has no race risk.

```bash
sqlcmd -S <server> -d <db> -i src/host/AqtCoreFW.Api/Scripts/backfill-case-workflow-instances.sql -o backfill-log-$(date +%F).txt
```

**Progress output (RAISERROR ... WITH NOWAIT flushes live):**

```
2026-04-17T10:00:01.1234567 | backfill batch=1 inserted=1000 total=1000
2026-04-17T10:00:01.4567890 | backfill batch=2 inserted=1000 total=2000
...
Backfill complete. Batches=N. TotalInserted=X
```

**Throughput expectation:** ~5k rows/sec on SQL Server 2022 / pooled connection → 100k Cases ≈ 20s.

**Inline parity check** (runs at the end of the script) must return **0 drift rows**. If any row returns, treat as P1 incident — backfill created mismatched state mappings.

---

## Post-backfill verification

All three assertions must hold:

```sql
-- 1. Count parity: every non-deleted Case has exactly 1 WorkflowInstance
SELECT
    (SELECT COUNT(*) FROM cases.Cases WHERE IsDeleted = 0)                                          AS CaseCount,
    (SELECT COUNT(*) FROM workflow.WorkflowInstances
      WHERE EntityType = 'Case' AND IsDeleted = 0)                                                  AS InstanceCount;
-- CaseCount must equal InstanceCount.

-- 2. Uniqueness: no duplicate instances per Case
SELECT EntityId, COUNT(*) AS N
FROM workflow.WorkflowInstances
WHERE EntityType = 'Case' AND IsDeleted = 0
GROUP BY EntityId HAVING COUNT(*) > 1;
-- Must return 0 rows.

-- 3. State parity (same as inline check): Case.Status = Workflow state name
SELECT c.[Status], ws.Name, COUNT(*) AS N
FROM cases.Cases c
JOIN workflow.WorkflowInstances wi ON wi.EntityType = 'Case' AND wi.EntityId = c.Id
JOIN workflow.WorkflowStates ws    ON ws.Id = wi.CurrentStateId
WHERE c.IsDeleted = 0 AND wi.IsDeleted = 0 AND c.[Status] <> ws.Name
GROUP BY c.[Status], ws.Name;
-- Must return 0 rows.
```

If any assertion fails → do NOT flip the feature flag. Open P1, investigate.

---

## Idempotency

Re-running the main script is safe: `NOT EXISTS` guard skips already-backfilled Cases. Operator should see `TotalInserted=0` if the entire dataset is covered.

---

## Rollback

**Trigger conditions (any one of):**

- Parity assertion fails post-backfill (P1).
- Feature flag flipped ON then transitions throw `GOV_WFL_010` (`InstanceNotFound`) at materially non-zero rate — indicates backfill missed rows for a tenant/segment.
- Correctness incident traced to Phase-05 → clean slate requested.

**Before rollback:** flip feature flag `feature:cases.workflow-delegation` = **false** (per tenant or global). This immediately freezes the delegation path; legacy `case.Submit/Approve/…` resumes ownership. See `docs/runbooks/cases-workflow-drift-check.md` § "Option C — Roll back to legacy path".

**Rollback execution:**

```bash
sqlcmd -S <server> -d <db> -i src/host/AqtCoreFW.Api/Scripts/rollback-case-workflow-instances.sql -o rollback-log-$(date +%F).txt
```

**Rollback output (example):**

```
Rollback complete. History rows deleted=0. Instance rows deleted=98,432.
RemainingCaseInstances=0. RemainingCaseHistory=0.
```

History rows expected = 0 under the backfill scope (fresh rows with no transitions yet). Non-zero = transitions happened via delegation path (flag was ON) — rollback still safe but incident scope widens.

**After rollback:** re-run dry-run Q3 → `MissingInstances = CaseCount`. Schema is back to pre-Phase-05 state.

---

## Deployment checklist (merge this into the prod release runbook)

- [ ] Phase-00 MasterData taxonomy migrated + seeder has run (`WorkflowStateTaxonomies` has `ReturnedForRevision` row).
- [ ] Phase-01 CaseProcess seeder has run — pre-flight Q1 shows 7 states.
- [ ] Phase-02/03/04 code deployed, feature flag `feature:cases.workflow-delegation` = **false**.
- [ ] Pre-flight dry-run passes all 4 queries (Q2 = 0 orphans).
- [ ] Main backfill runs, inline parity = 0 drift.
- [ ] Post-backfill verification passes (count + unique + state parity).
- [ ] 48 hours of reconciliation drift-check (`docs/runbooks/cases-workflow-drift-check.md`) returns 0.
- [ ] Feature flag flipped ON, 1% canary tenant → 24h soak → full ramp.

---

## Change log

| Date       | Change                                                       |
|------------|--------------------------------------------------------------|
| 2026-04-17 | Initial runbook created in Phase-05 deliverables.            |
