---
title: Phase 5b SQL Audit Kit — Empty Initial-State Handler Pool Pre-Flight
slug: audit-260522-0834-phase-05b-empty-pool-preflight
date: 2026-05-22
status: READY_FOR_OPS
---

# Phase 5b SQL Audit Kit — Empty Initial-State Handler Pool Pre-Flight

**Purpose:** Identify existing production `WorkflowDefinition` rows where the INITIAL state has an empty handler pool. These will hard-block instance creation with `GOV_WFL_014 NoHandlerResolved` (HTTP 422) after Phase 5b (PR #113, merge commit `70465c08`, ships 2026-05-22) reaches production. Ops MUST run this BEFORE deploy to identify activations that may break.

**Related:** Companion to [Phase 5c audit kit](../../plans/reports/audit-260521-1715-phase-05c-graph-validation-preflight.md) (R3 invalid PermCodes / R4 NotSupported modes at activation). If 5c kit shows violations, 5b is blocked anyway — fix 5c findings first.

---

> ## ⚠️ Update 2026-05-26 — all clause modes are now supported producers
>
> Clause modes `2=PreviousHandler`, `4=ByRole`, `5=ByGroup`, `6=ByOrgUnit` are **no longer NotSupported** — each shipped a producer (PreviousHandler, ByRole, ByGroup, ByOrgUnit). The resolver no longer throws `NotSupportedException` for them. This changes the audit semantics:
>
> - **Q3 / Q5** below were written to flag clause modes `IN (2,4,5,6)` as categorically empty. **That logic is now stale and will produce FALSE POSITIVES** (flagging valid definitions as broken). The mode-list filters in Q3/Q5 have been updated; Q5 is **deprecated** (no clause-level NotSupported mode remains).
> - The ONLY remaining statically-queryable categorically-empty construct is **`CombineMode=1` (Intersect)** — still rejected by the resolver. Q3 now flags Intersect only.
> - Modes 2/4/5/6 can still produce a **data-dependent** empty pool at runtime (e.g., ByOrgUnit with no active temporal assignment, ByRole with no users in the role, ByGroup with no members, PreviousHandler at the initial state with no prior completer). These are **NOT statically queryable** from the definition schema — they need cross-module joins to live Identity/Org data — and are caught at runtime by the 5b fail-CLOSED path (422, no leak), exactly like the Initiator-null case in §2.

---

## 1. Schema Scout — Table/Column Names & Types

### WorkflowDefinition
- **Table:** `workflow.WorkflowDefinitions`
- **Columns relevant to audit:**
  - `Id` (bigint, PK)
  - `PublicId` (uniqueidentifier, AK)
  - `TenantId` (uniqueidentifier) — multi-tenant predicate
  - `Name` (nvarchar(200)) — for human-readable triage
  - `IsActive` (bit) — only active definitions matter (inactive cannot be instantiated)

### WorkflowState
- **Table:** `workflow.WorkflowStates`
- **Columns relevant to audit:**
  - `Id` (bigint, PK)
  - `DefinitionId` (bigint, FK to WorkflowDefinition.Id)
  - `IsInitial` (bit) — Phase 5b only triggers on the INITIAL state
  - `IsFinal` (bit) — final-state initials are degenerate; not subject to 5b (no creation flow)
  - `AutoTransitionOnTimeoutId` (bigint, nullable) — Pool(S) excludes auto-timeout transition

### WorkflowTransition
- **Table:** `workflow.WorkflowTransitions`
- **Columns relevant to audit:**
  - `Id` (bigint, PK)
  - `DefinitionId` (bigint, FK)
  - `FromStateId` (bigint, FK to WorkflowState.Id)
  - `IsRecall` (bit) — Pool(S) excludes recall transitions

### WorkflowTransitionAssignment
- **Table:** `workflow.WorkflowTransitionAssignments`
- **Columns relevant to audit:**
  - `Id` (bigint, PK)
  - `WorkflowTransitionId` (bigint, FK, UNIQUE — 1:1 per transition)
  - `CombineMode` (int enum) — 0=Union, 1=**Intersect (NotSupported by resolver — throws)**

### WorkflowTransitionAssignmentClause
- **Table:** `workflow.WorkflowTransitionAssignmentClauses`
- **Columns relevant to audit:**
  - `Id` (bigint, PK)
  - `AssignmentId` (bigint, FK)
  - `Mode` (int enum) — ALL clause modes are now supported producers: `0=SpecificUser, 1=Initiator, 2=PreviousHandler, 3=ByPermission, 4=ByRole, 5=ByGroup, 6=ByOrgUnit`. (Modes 2/4/5/6 no longer throw `NotSupportedException` — see the update banner above.) Modes 2/4/5/6 can resolve to data-dependent empty pools (no active member/assignment), caught at runtime, not statically queryable here.
  - `RefsJson` (nvarchar, nullable) — for `Mode=3` (ByPermission): JSON array of perm codes; null/`[]` resolves to empty set

---

## 2. Resolution Semantics — "Empty Pool" in SQL Terms

**Model B resolver** (`IWorkflowHandlerResolver`, integrated into `CreateWorkflowInstanceCommandHandler.cs:56-84` and `InProcessWorkflowModuleClient.CreateInstanceAsync`):

```
Pool(initialState) = ⋃ (over outgoing eligible transitions) resolved clause-handlers
  where eligible = NOT IsRecall AND NOT state.AutoTransitionOnTimeoutId
```

**Phase 5b hard-block** at `CreateWorkflowInstanceCommandHandler.cs:83`:
```csharp
if (resolution.IsEmpty)
    return WorkflowInstanceResult.Fail(WorkflowErrors.NoHandlerResolved(initialState.Id));
```
→ HTTP 422, `GOV_WFL_014`, dual-metadata (4-B-A invariant).

**A definition fails 5b creation when ANY of these holds for its initial non-final state:**

| Class | Condition | SQL queryable? |
|---|---|---|
| Q1 | Initial state has 0 eligible outgoing transitions | YES |
| Q2 | Outgoing transitions exist but ALL lack `WorkflowTransitionAssignment` rows | YES |
| Q3 | Assignment uses `CombineMode=1` (Intersect) — the only remaining categorically-NotSupported construct (clause modes 2/4/5/6 are now supported) | YES |
| Q4 | Clauses are exclusively `Mode=3` (ByPermission) with `RefsJson IS NULL OR RefsJson = '[]'` | YES |
| (info) | Modes 2/4/5/6 resolving to an empty pool (no active member/assignment/prior-completer) | NO — data-dependent, needs cross-module Identity/Org joins |
| (info) | `Mode=1` (Initiator) with null `InitiatorUserId` at creation time | NO — runtime context, not in DB |
| (info) | `Mode=0` (SpecificUser) with `RefsJson` Guid referencing deleted user | NO — cross-module User table join discouraged |

Initiator-null and stale-user edge cases are caught at runtime by 5b fail-CLOSED → 422 with no leak. SQL kit targets the ~95% schema-queryable subset.

---

## 3. Queries (run in order; each returns 0 rows in a clean environment)

> **Multi-tenant:** Add `AND d.TenantId = @TenantId` to scope to a specific tenant. Omit to scan globally.
> **All queries are READ-ONLY (SELECT-only).** Safe to run against any environment.

### Q1 — Initial states with ZERO eligible outgoing transitions

**Why:** Pool(S) = ∅ trivially. Creation hard-blocks immediately.

```sql
WITH InitialStates AS (
    SELECT
        s.Id              AS InitialStateId,
        s.DefinitionId,
        s.AutoTransitionOnTimeoutId,
        COUNT(t.Id)       AS EligibleOutgoingCount
    FROM workflow.WorkflowStates s
    LEFT JOIN workflow.WorkflowTransitions t
        ON t.FromStateId = s.Id
        AND t.IsRecall = 0
        AND (s.AutoTransitionOnTimeoutId IS NULL OR s.AutoTransitionOnTimeoutId <> t.Id)
    WHERE s.IsInitial = 1
      AND s.IsFinal = 0
    GROUP BY s.Id, s.DefinitionId, s.AutoTransitionOnTimeoutId
    HAVING COUNT(t.Id) = 0
)
SELECT
    d.Id, d.PublicId, d.Name, d.TenantId, d.IsActive,
    i.InitialStateId,
    'Q1_ZERO_OUTGOING' AS Violation
FROM InitialStates i
INNER JOIN workflow.WorkflowDefinitions d ON i.DefinitionId = d.Id
WHERE d.IsActive = 1
-- AND d.TenantId = @TenantId    -- per-tenant scoping
ORDER BY d.TenantId, d.Id;
```

### Q2 — Initial states whose eligible outgoing transitions ALL lack assignments

**Why:** Without an assignment row, the transition contributes nothing to Pool(S).

```sql
WITH EligibleTransitions AS (
    SELECT t.Id, t.FromStateId, t.DefinitionId
    FROM workflow.WorkflowTransitions t
    INNER JOIN workflow.WorkflowStates s ON t.FromStateId = s.Id
    WHERE s.IsInitial = 1
      AND s.IsFinal = 0
      AND t.IsRecall = 0
      AND (s.AutoTransitionOnTimeoutId IS NULL OR s.AutoTransitionOnTimeoutId <> t.Id)
)
SELECT
    d.Id, d.PublicId, d.Name, d.TenantId, d.IsActive,
    s.Id AS InitialStateId,
    COUNT(et.Id)                                   AS OutgoingCount,
    SUM(CASE WHEN a.Id IS NULL THEN 0 ELSE 1 END)  AS AssignmentCount,
    'Q2_OUTGOING_NO_ASSIGNMENTS' AS Violation
FROM workflow.WorkflowStates s
INNER JOIN workflow.WorkflowDefinitions d ON s.DefinitionId = d.Id
INNER JOIN EligibleTransitions et            ON et.FromStateId = s.Id
LEFT  JOIN workflow.WorkflowTransitionAssignments a
       ON a.WorkflowTransitionId = et.Id
WHERE s.IsInitial = 1
  AND s.IsFinal = 0
  AND d.IsActive = 1
-- AND d.TenantId = @TenantId
GROUP BY d.Id, d.PublicId, d.Name, d.TenantId, d.IsActive, s.Id
HAVING SUM(CASE WHEN a.Id IS NULL THEN 0 ELSE 1 END) = 0
ORDER BY d.TenantId, d.Id;
```

### Q3 — Initial states whose eligible-transition assignment uses CombineMode=Intersect

**Why:** The resolver still throws `NotSupportedException` for `CombineMode=1` (Intersect). The 5b catch-all maps to `GOV_WFL_014`. (Clause modes 2/4/5/6 are NO LONGER NotSupported — the old "all clauses are NotSupported modes" arm has been removed to avoid false positives; see the update banner.)

```sql
WITH EligibleTransitions AS (
    SELECT t.Id, t.FromStateId, t.DefinitionId
    FROM workflow.WorkflowTransitions t
    INNER JOIN workflow.WorkflowStates s ON t.FromStateId = s.Id
    WHERE s.IsInitial = 1
      AND s.IsFinal = 0
      AND t.IsRecall = 0
      AND (s.AutoTransitionOnTimeoutId IS NULL OR s.AutoTransitionOnTimeoutId <> t.Id)
),
ClauseDigest AS (
    SELECT
        a.WorkflowTransitionId,
        a.CombineMode,
        SUM(CASE WHEN c.Mode IN (0, 1, 3) THEN 1 ELSE 0 END) AS MvpClauseCount,
        SUM(CASE WHEN c.Mode IN (2, 4, 5, 6) THEN 1 ELSE 0 END) AS NotSupportedClauseCount,
        COUNT(c.Id) AS TotalClauseCount
    FROM workflow.WorkflowTransitionAssignments a
    LEFT JOIN workflow.WorkflowTransitionAssignmentClauses c ON c.AssignmentId = a.Id
    GROUP BY a.WorkflowTransitionId, a.CombineMode
)
SELECT
    d.Id, d.PublicId, d.Name, d.TenantId, d.IsActive,
    s.Id AS InitialStateId,
    cd.WorkflowTransitionId,
    cd.CombineMode,
    cd.MvpClauseCount,
    cd.NotSupportedClauseCount,
    'Q3_INTERSECT_COMBINE' AS Violation
FROM workflow.WorkflowStates s
INNER JOIN workflow.WorkflowDefinitions d ON s.DefinitionId = d.Id
INNER JOIN EligibleTransitions et            ON et.FromStateId = s.Id
INNER JOIN ClauseDigest cd                   ON cd.WorkflowTransitionId = et.Id
WHERE s.IsInitial = 1
  AND s.IsFinal = 0
  AND d.IsActive = 1
  AND cd.CombineMode = 1                                            -- Intersect = NotSupported (only remaining categorical block)
-- AND d.TenantId = @TenantId
ORDER BY d.TenantId, d.Id;
```

### Q5 — DEPRECATED (mixed-mode MVP + NotSupported clauses)

**Deprecated 2026-05-26:** This query detected `WorkflowTransitionAssignment` rows mixing MVP clauses (Mode ∈ {0,1,3}) with then-NotSupported clauses (Mode ∈ {2,4,5,6}) — a latent runtime `NotSupportedException`. Since modes 2/4/5/6 are now supported producers, **no clause-level NotSupported mode remains**, so this query has no failure mode to detect and would only produce false positives. Retained below for historical reference only; **do not run it as a deploy gate.**

```sql
WITH EligibleTransitions AS (
    SELECT t.Id, t.FromStateId, t.DefinitionId
    FROM workflow.WorkflowTransitions t
    INNER JOIN workflow.WorkflowStates s ON t.FromStateId = s.Id
    WHERE s.IsInitial = 1
      AND s.IsFinal = 0
      AND t.IsRecall = 0
      AND (s.AutoTransitionOnTimeoutId IS NULL OR s.AutoTransitionOnTimeoutId <> t.Id)
),
MixedModeAssignments AS (
    SELECT
        a.Id AS AssignmentId,
        a.WorkflowTransitionId,
        SUM(CASE WHEN c.Mode IN (0, 1, 3) THEN 1 ELSE 0 END) AS MvpClauseCount,
        SUM(CASE WHEN c.Mode IN (2, 4, 5, 6) THEN 1 ELSE 0 END) AS NotSupportedClauseCount
    FROM workflow.WorkflowTransitionAssignments a
    INNER JOIN workflow.WorkflowTransitionAssignmentClauses c ON c.AssignmentId = a.Id
    GROUP BY a.Id, a.WorkflowTransitionId
    HAVING SUM(CASE WHEN c.Mode IN (0, 1, 3) THEN 1 ELSE 0 END) > 0
       AND SUM(CASE WHEN c.Mode IN (2, 4, 5, 6) THEN 1 ELSE 0 END) > 0
)
SELECT
    d.Id, d.PublicId, d.Name, d.TenantId, d.IsActive,
    s.Id AS InitialStateId,
    mma.WorkflowTransitionId,
    mma.AssignmentId,
    mma.MvpClauseCount,
    mma.NotSupportedClauseCount,
    'Q5_MIXED_MODE_LATENT_THROW' AS Violation
FROM workflow.WorkflowStates s
INNER JOIN workflow.WorkflowDefinitions d ON s.DefinitionId = d.Id
INNER JOIN EligibleTransitions et            ON et.FromStateId = s.Id
INNER JOIN MixedModeAssignments mma           ON mma.WorkflowTransitionId = et.Id
WHERE s.IsInitial = 1
  AND s.IsFinal = 0
  AND d.IsActive = 1
-- AND d.TenantId = @TenantId
ORDER BY d.TenantId, d.Id;
```

### Q4 — Initial states whose ByPermission clauses have null/empty `RefsJson`

**Why:** `Mode=3` (ByPermission) parses `RefsJson` as a JSON array of perm codes; null/`[]` resolves to an empty handler set → Pool contribution is empty.

```sql
WITH EligibleTransitions AS (
    SELECT t.Id, t.FromStateId, t.DefinitionId
    FROM workflow.WorkflowTransitions t
    INNER JOIN workflow.WorkflowStates s ON t.FromStateId = s.Id
    WHERE s.IsInitial = 1
      AND s.IsFinal = 0
      AND t.IsRecall = 0
      AND (s.AutoTransitionOnTimeoutId IS NULL OR s.AutoTransitionOnTimeoutId <> t.Id)
)
SELECT
    d.Id, d.PublicId, d.Name, d.TenantId, d.IsActive,
    s.Id AS InitialStateId,
    et.Id AS WorkflowTransitionId,
    c.Id  AS ClauseId,
    c.Mode,
    c.RefsJson,
    'Q4_BY_PERMISSION_EMPTY_REFS' AS Violation
FROM workflow.WorkflowStates s
INNER JOIN workflow.WorkflowDefinitions d ON s.DefinitionId = d.Id
INNER JOIN EligibleTransitions et            ON et.FromStateId = s.Id
INNER JOIN workflow.WorkflowTransitionAssignments a ON a.WorkflowTransitionId = et.Id
INNER JOIN workflow.WorkflowTransitionAssignmentClauses c ON c.AssignmentId = a.Id
WHERE s.IsInitial = 1
  AND s.IsFinal = 0
  AND d.IsActive = 1
  AND c.Mode = 3
  AND (c.RefsJson IS NULL OR LTRIM(RTRIM(c.RefsJson)) IN ('', '[]'))
-- AND d.TenantId = @TenantId
ORDER BY d.TenantId, d.Id;
```

### Q6 — Orphan WorkflowTasks with `WorkflowInstanceId` not present in `WorkflowInstances` (PHR-V2 P6 S4 FK pre-flight)

**Why:** P6 S4 (commit `d3f54c28`, migration `20260522063712_AddWorkflowTaskInstanceNavProp`) adds a DB-level FK constraint on `workflow.WorkflowTasks.WorkflowInstanceId → workflow.WorkflowInstances.Id`. The column already existed but was unconstrained — any orphan row (e.g., `WorkflowInstanceId = 0` produced by pre-5b code paths before the IDENTITY-propagation fix landed) will block migration apply with `SqlException 547` ("INSERT or UPDATE … conflicts with FOREIGN KEY constraint"). Run this query in EVERY non-clean environment before applying the P6 S4 migration.

```sql
-- Surfaces every orphan task irrespective of soft-delete / tenant filters.
SELECT
    t.Id,
    t.PublicId,
    t.WorkflowInstanceId,
    t.TenantId,
    t.Status,
    t.CreatedAt,
    'Q6_ORPHAN_TASK_FK' AS Violation
FROM workflow.WorkflowTasks t WITH (NOLOCK)
LEFT JOIN workflow.WorkflowInstances i WITH (NOLOCK) ON i.Id = t.WorkflowInstanceId
WHERE i.Id IS NULL
ORDER BY t.TenantId, t.Id;
```

**Remediation:**
- If Q6 returns rows AND they are pre-5b legacy data (FK = 0 or matches a hard-deleted instance), reconcile per env: soft-delete the orphan tasks (`UPDATE … SET IsDeleted = 1`) OR raw `DELETE` after stakeholder sign-off (audit trail loss acceptable for FK = 0 rows).
- Re-run Q6 → expect 0 rows.
- Apply migration; verify `sys.foreign_keys` shows `FK_WorkflowTasks_WorkflowInstances_WorkflowInstanceId`.

---

## 4. Interpretation & Dispositions

| Result | Meaning | Disposition |
|---|---|---|
| All queries empty | Clean — no definitions break under 5b hard-block | Proceed with deploy |
| Q1 non-empty | Initial states with zero outgoing → degenerate definition (dead-end at start) | BP redesign required; possibly deactivate definition |
| Q2 non-empty | Outgoing exists but no `WorkflowTransitionAssignment` rows seeded | Data-fix: add `WorkflowTransitionAssignment` + clauses for each transition |
| Q3 non-empty | Assignment uses `CombineMode=Intersect` — still NotSupported by the resolver | Data-fix: switch to `CombineMode=Union` OR deactivate definition |
| Q4 non-empty | ByPermission with no perm codes — likely misconfiguration or stale data | Data-fix: populate RefsJson or deactivate definition |
| Q5 (DEPRECATED) | No longer a failure mode — modes 2/4/5/6 are supported producers | Do not run as a gate; ignore results |
| Q6 non-empty | Orphan task referencing a non-existent `WorkflowInstanceId` — blocks P6 S4 FK migration (`SqlException 547`) | Reconcile orphans (soft-delete OR raw DELETE) before applying migration `20260522063712_AddWorkflowTaskInstanceNavProp` |

If violations exist:
1. Snapshot results into a deployment-blocker ticket; share with workflow ops + product owner.
2. Choose: data-fix / definition redesign / Phase 5d deferral / waiver.
3. Re-run all 4 queries to confirm zero rows.
4. Document the decision before shipping 5b to prod.

---

## 5. Caveats

- **Tenant-scoped data:** Multi-tenant runs SHOULD filter by `@TenantId` to reduce noise and to surface tenant-specific failures.
- **Inactive definitions:** Filtered out (`d.IsActive = 1`); inactive definitions cannot be instantiated, so they're not 5b risks today (re-audit on re-activation).
- **Initiator-null and stale-SpecificUser-Guid:** Not queryable statically. Runtime fail-CLOSED catches them (422, no leak).
- **Test seed data:** Dev/staging DBs may contain test definitions with degenerate pools. Either run against a clean test DB or filter `TenantId NOT IN (<test tenant Guids>)`.
- **5c kit dependency:** If [5c kit](../../plans/reports/audit-260521-1715-phase-05c-graph-validation-preflight.md) Q1 (invalid ByPermission codes) shows violations, 5b is blocked irrespective of these queries — fix 5c first.
- **Mixed-mode latent throw (Q5) — DEPRECATED 2026-05-26:** Originally Q5 detected assignments mixing MVP and then-NotSupported clauses (latent runtime throw). All clause modes are now supported producers, so this failure mode no longer exists. Q5 is retained for history only; do not run it as a gate.
- **Data-dependent empty pools (modes 2/4/5/6):** Now that these modes are supported, a structurally-valid definition can still resolve to an empty initial-state pool when the live data is empty (no active OrgUnit assignment / no role members / no group members / no prior completer at the initial state). This is NOT statically queryable here (needs cross-module Identity/Org joins); the 5b fail-CLOSED path catches it at runtime (422, no leak), same as Initiator-null.

---

## 6. Sign-off Template

```
[5b PRE-FLIGHT AUDIT] @<run-date> @<env>
  Q1 (zero outgoing):                 <count> rows
  Q2 (no assignments):                <count> rows
  Q3 (Intersect combine):             <count> rows
  Q4 (empty ByPermission refs):       <count> rows
  Q5 (DEPRECATED — skip):             n/a
  Q6 (orphan task FK, P6 S4 deploy):  <count> rows
  Disposition:                        <PROCEED | BLOCK | WAIVER #...>
  Reviewer:                           <name>
```
