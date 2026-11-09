# Org-Unit Data Scoping Advisory→Planning Session

**Date**: 2026-07-30 15:17 Asia/Saigon  
**Severity**: High  
**Component**: Org-tree, RLS, cross-module scoping, data visibility workflow  
**Status**: DONE (plan-on-shelf, gated until v1.0 launch stable + 2 code blockers resolved)

## What Happened

User question: "Does the system solve the scenario where 1 account views data from 1+ organizational units (đơn vị) within a tenant, assignable to user OR group?"

Answered via 3-round advisory session (3× scout + 2× @kongming strongest-model advisor):
1. **Infrastructure audit** — confirmed OrgUnit self-referential tree + UserOrgUnitAssignment (temporal, multi-assignment) + RBAC + DataScopeType design exist in codebase.
2. **RLS audit** — discovered: record-level org scoping NOT WIRED. Current RLS is TenantId-only. WorkflowInstances table has NO RLS at all. Group→unit grant table absent (group scope today goes through RoleDataScope.ScopeValue string-Guid, unusable in RLS).
3. **Product decisions locked** — user confirmed 3 requirements: (a) transfer visibility = participant-set model (CurrentUnitId + WorkflowInstanceUnitParticipants table); (b) unit dissolution = data-migration via existing OrgUnit.SuccessorId; (c) surface = full 2×2 matrix (ORG_UNIT + ORG_TREE × user + group), effective visibility = UNION.

**Design chosen (DB-layer RLS, scalar SESSION_CONTEXT + closure semi-join):**
- UserId as scalar SESSION_CONTEXT.UserId (fail-closed interceptor).
- Org scope via EXISTS semi-join through security.OrgUnitClosure (self-row + Depth descendants).
- Group memberships joined LIVE via identity.UserGroupMemberships (no cache, fresh every query).
- Participant-set as separate FILTER predicate (union with org scope).
- security.{User,Group}OrgScopeGrants tables (group grants wired to UNION of all groups).
- FILTER + BLOCK-AFTER-INSERT only; dedicated policy with independent STATE=OFF kill-switch.
- Interceptor sets UserId fail-closed; 6 Hangfire jobs flagged for bypass annotation.

**Riskiest item identified:** write-path blindness. Fail-closed FILTER → forgotten participant row = silent invisible data (case transfer doesn't propagate participant). Mitigation: reconciler invariant (audit log on stale participants) + build-time call-site census (grep-based enforcement).

**Deliverables produced:**
- Advisory report: `C:\AqtCoreFW\plans\260730-1506-org-unit-data-scoping-workflow-pilot\reports\advise-260730-1506-org-unit-data-scoping-workflow-pilot-report.md`
- 8-phase plan skeleton: `C:\AqtCoreFW\plans\260730-1506-org-unit-data-scoping-workflow-pilot\`
- Backlog row: ORG-UNIT-DATA-SCOPING-WORKFLOW-PILOT (T3 post-v1.0, gated on launch stable).

**Planner cross-check flagged 1 new seam:** OrgUnit.Id = bigint vs cross-module IOrgUnitModuleClient PublicId = Guid. Guid↔bigint mapping is a MUST-VERIFY point if any cross-module client calls OrgUnit APIs. Documented in plan as risk verify-step.

## The Brutal Truth

This session exposed how much guesswork was embedded in the existing org-tree design. The code has structure (tree, assignments, RBAC enum) but the RLS is completely absent — WorkflowInstances has no scoping at all. That means the feature we shipped (gov workflow case assignment, multi-unit transfers, participant tracking) has NO ACCESS CONTROL at the data tier. Any user in a tenant sees all workflow cases regardless of unit assignment. This is a live security hole.

The frustrating part is that the org infrastructure EXISTS (self-ref tree, temporal multi-assignment table, DataScopeType enum) but it's DISCONNECTED from any filter logic. It's like wiring was installed but never plugged in. The closure-table design is heavy but necessary because the ORG_TREE traversal (get all descendants to show "current unit + subordinate units") can't fit in 8000 bytes of SESSION_CONTEXT with large hierarchies (L1 user sees ~1000+ org nodes). So we can't take the easy path (unit-list in SQL_VARIANT).

The user made clear product choices (participant-set visibility model for case transfers, not single-owner column) that are CORRECT given the gov workflow semantics (chuyển hồ sơ = re-assign case to another unit, case must stay visible to originating unit until closure). That choice prevents silent invisibility on transfer, which is good governance. But it does mean the write-path needs guardian rails (the reconciler + build-time census) to prevent forgotten participant rows.

## Technical Details

**6 Medium-Confidence Assumptions Carried as Verify-Steps:**

1. **SuccessorId already exists on OrgUnit.** Verified on source: exists (int nullable), semantics = "if this unit merges, redirect to this unit." Used today (0 populated in seed, so logic untested). Needs integration test when unit-dissolution is wired.

2. **OrgUnit.Id is bigint, IOrgUnitModuleClient.PublicId is Guid.** Verified on source. Mapping layer = Guid→bigint resolution in API boundary. Cross-module callers MUST NOT call org-unit APIs directly; must go through ApiClient contract. Planner flagged: if any module calls OrgUnitRepository directly, bigint↔Guid drift = STOP.

3. **ByOrgUnit resolver (AssignmentClauseMode.cs) is NOT a stub.** Verified: shipped Phase 04 RT2-15 (active code, not comment). But XML doc (lines 12–14) claims "future" and assignment-authorization is orthogonal to data-scope. This is a nomenclature hazard: resolver exists but doesn't do RLS. Rename needed to avoid future confusion (suggest: `OrgUnitAssignmentResolver` vs `OrgUnitDataScopeResolver`).

4. **GetOrgUnitHierarchy returns ancestors, NOT descendants.** Verified on source. But for ORG_TREE visibility (show current unit + subordinates), we need descendants. Plan calls for closure table (self-row + all Depth rows) computed offline or via CTE. Cannot use existing GetOrgUnitHierarchy without redesign.

5. **Group→unit grant table absent.** Verified: no `UserGroupOrgUnitAssignments` or similar table. Groups today go through RoleDataScope.ScopeValue (string-Guid, unstructured). Must CREATE table + FK + migration. Estimate: 2 rows (User, Group grant tables), 2 migrations.

6. **Participant-set model requires WorkflowInstanceUnitParticipants table.** Verified: does NOT exist today. CREATE needed. Estimate: 1 table + 1 migration.

**Decision Points (Why Not Alternative Paths):**

- **Why NOT SESSION_CONTEXT unit-list transport:** SQL_VARIANT 8000-byte cap. Leadership user (L1) with ~1000 descendant org nodes = ~20k JSON stringified. Exceeds cap. Closure semi-join scales to any hierarchy depth without transport limit.

- **Why participants-set not single-owner column:** Gov case-transfer (chuyển hồ sơ) is core. Single-column + FILTER visibility model hides case from originating unit post-transfer (case owner = new unit, FILTER = new unit only, originating unit can no longer see). That breaks case audit trail and originating-unit accountability. Participant-set (UNION visibility) preserves audit trail.

- **Why FILTER + BLOCK-AFTER-INSERT, not FILTER-ONLY:** FILTER allows hidden rows to exist in queries; BLOCK-AFTER-INSERT prevents INSERT into out-of-scope units. Both needed for safety (query visibility + write guard). Kill-switch (STATE=OFF) is single independent flag for operational override.

## What We Tried

**Rejected: cache group memberships.** Would require invalidation on UserGroupMembership change + background job to refresh. LIVE join via UserGroupMemberships is simpler, single source of truth, no stale-cache risk. Cost: 1 extra join per query. Acceptable for admin/case-access patterns.

**Rejected: single DATA_SCOPE_TYPE enum value.** User requirements = 2×2 matrix (ORG_UNIT + ORG_TREE × user + group) all surfaced + effective = UNION. Collapsed to single enum value would hide group grants or force complex CASE logic. Four separate predicates (user-org, user-tree, group-org, group-tree) with UNION is clearer.

**Rejected: participant-set as nullable FK on WorkflowInstance.** Would force schema migration on every case-transfer (participant row = case's "current unit"). Instead: separate table (WorkflowInstanceUnitParticipants) + FILTER predicate. Avoids wide-schema pollution, independent lifecycle.

## Root Cause Analysis

**Why org scoping wasn't wired:** Original design (2026 Q1) built OrgUnit tree + DataScopeType enum for FUTURE use. No RLS requirement was active; focus was assignment-authz (user → role, role → workflow-action). Org-tree existed as infrastructure but was never connected to record-level access control. RLS today is TenantId-only (coarse). WorkflowInstances has NO RLS at all — that's the hole.

**Why this surfaced now:** User's gov-workflow scenario (multi-unit case-transfer + participant tracking) requires fine-grain access control. Discovered org infra exists but is disconnected. Honest option: wire it now (12–15 dev-days, gated post-v1.0 launch). Rushed alternative: fake it with query-layer filters (fragile, leaky, auditable). User chose to plan it properly.

## Lessons Learned

1. **Infrastructure without wiring is an operational liability.** OrgUnit tree + DataScopeType design sat for 6 months. Anyone reading the enum assumes it's plugged in; it's not. Next time: design-guidelines must name when a feature is "infrastructure only, no policy enforcement yet." Add a linting check (grep + TODO marker).

2. **Org hierarchies don't fit in session-context.** SQL_VARIANT 8000-byte cap is a hard limit. Leadership users with large subtrees = guaranteed overflow. Closure tables are heavier (storage, maintenance) but scale correctly. Lesson: for hierarchical scoping, design closure-table from day 1, not as a retrofit.

3. **Participant-set visibility requires reconciliation.** Fail-closed FILTER hides stale rows. Without a reconciler audit-log (when participants are orphaned post-transfer), silent invisibility is undetectable. Add reconciler invariant to every feature that uses participant-set. Build-time census (grep for WorkflowInstanceUnitParticipants writes) is a backstop.

4. **Cross-module Guid↔bigint mapping is a footgun.** If module A uses Guid(PublicId) but org-module uses bigint(Id), the API boundary MUST resolve the mapping. If a caller goes direct to repo (bypassing API), the mapping is lost. Enforce: "only clients call org-module via ApiClient; repos are internal." Add arch-test.

5. **Rename for clarity, not just functionality.** AssignmentClauseMode.ByOrgUnit XML doc says "future" and is misleading. Name should distinguish "resolver exists but doesn't enforce RLS yet." Or: rename to OrgUnitAssignmentAuthzResolver (clear: authorization only, no RLS). Prevents future contributors from assuming RLS is wired.

## Next Steps

1. **Create consolidated advisory → plan document.** (DONE) Advise report + phase skeleton shipped to `plans/260730-1506-org-unit-data-scoping-workflow-pilot/`.

2. **Add backlog row.** (DONE) ORG-UNIT-DATA-SCOPING-WORKFLOW-PILOT, T3 post-v1.0, gated on:
   - v1.0 launch stable + 2 code blockers resolved (SIGN-FULL-PDF-CRYPTO-VERIFY, CI-DISABLED-SINCE-APRIL).
   - Verify-step: Guid↔bigint mapping audit (no cross-module direct-repo calls).
   - Verify-step: SuccessorId integration test on unit merge.

3. **FU: dependency-map cross-module seams audit.** Plan calls for cross-module org-client contract; verify all callers use ApiClient, not direct-repo. Add arch-test to enforce.

4. **FU: RLS security-guidelines update.** Document closure-table pattern, participant-set visibility, fail-closed filters, reconciler invariants. Update design-guidelines.md and code-standards.md.

5. **When unblocked (post-v1.0 + code blockers):** Begin Phase 01 (closure-table migration + schema DDL for grants tables). Effort estimate = 12–15 dev-days (8 phases × 1.5–2 days each, with security-gate verification between phases).

---

**Status:** DONE  
**Summary:** Advisory session locked 3 product decisions (participant-set visibility, successor-id dissolution, 2×2 matrix effective union). Org-tree infrastructure verified wired, RLS absent. Design chosen: scalar UserId + closure semi-join + live group-membership + fail-closed filters. Plan on shelf, gated until v1.0 stable + 2 code blockers. Five verify-steps + one reconciler-invariant + one arch-test needed before phase 01.  
**Plan:** `C:\AqtCoreFW\plans\260730-1506-org-unit-data-scoping-workflow-pilot/`  
**Backlog row:** ORG-UNIT-DATA-SCOPING-WORKFLOW-PILOT
