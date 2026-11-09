# Phase-07 Tier C FU1 — Fixture Drift Post-Cases Removal — Technical Journal

**Date:** 2026-05-08 12:02 ICT
**Commit:** `ed375e94` (local, unpushed on main)
**Backlog Closes:** PHASE7-TIER-C-FU1 (P2)
**Status:** SHIPPED (local commit ready for push + runtime verification)

---

## Executive Summary

Backlog item PHASE7-TIER-C-FU1 (P2) — `POST /api/v1/workflow/definitions` returning 422 on `workflow-dynamic-instances.spec.ts` setup, cascading 13 gated tests across Tier C. Single fixture item masked **four distinct drift bugs**. All rooted in Cases module removal (2026-05-01): Entity enum registry, state names, request DTO field renames, UUID typing.

---

## Root Cause: Four Drift Surfaces

### 1. Missing `entityType` Request Body

**Premise:** `CreateWorkflowDefinitionRequest` requires `entityType` field. Spec sent `null`.

**Why:** Cases module was removed entirely 2026-05-01 (PR #71, squash `fca8a9ef`). After removal, `GenericEntityStatusEnumContributor` registered as ONLY contributor with allowed states `[Draft, Submitted, InReview, Approved, Rejected, Cancelled]`. Spec never updated to specify `entityType: 'Generic'`.

**Source verification:** `src/Modules/Workflow.Application/Commands/CreateWorkflowDefinitionCommand/CreateWorkflowDefinitionCommandValidator.cs` + `src/Infrastructure/EntityStatusEnumRegistry/GenericEntityStatusEnumContributor.cs`.

### 2. Stale State Name `Review`

**Premise:** Enum registry defines state as `InReview`, not `Review`.

**Why:** Spec hardcoded `state: 'Review'` — outdated naming. `GenericEntityStatusEnumContributor` only allows `[Draft, Submitted, InReview, Approved, Rejected, Cancelled]`. Validator rejected unknown state name.

### 3. Wrong Request DTO Field: `workflowDefinitionId`

**Premise:** `CreateWorkflowInstanceRequest` has field `definitionId` (was renamed during retier wave).

**Why:** `workflow-instances-tasks.spec.ts` Spec2 sent `body: { workflowDefinitionId }` — old field name. Controller record signature changed to `definitionId`. Model binder silently dropped unknown field → validation fail.

### 4. Wrong Entity Type: String vs UUID

**Premise:** `CreateWorkflowInstanceRequest.entityId` expects `string` (UUID).

**Why:** Spec Spec2 sent `entityId: 'string-literal'` instead of `crypto.randomUUID()`. Type mismatch.

---

## Files Changed (2 specs, zero prod code)

| File | Changes |
|------|---------|
| `web/e2e/workflow-dynamic-instances.spec.ts` | Add `entityType: 'Generic'` to POST body; rename graph state `Review` → `InReview` in request |
| `web/e2e/workflow-instances-tasks.spec.ts` | Rename body field `workflowDefinitionId` → `definitionId`; entityId: `crypto.randomUUID()` (was string) |
| `backlogs/backlog.md` | Mark CLOSED |
| `backlogs/completed.md` | Log with all 4 drift surfaces documented |
| `docs/development-roadmap.md` | Tier C progress sync |
| `docs/project-changelog.md` | Add entry |

---

## Test Results

| Check | Status |
|-------|--------|
| `tsc --noEmit` (web/) | ✅ exit 0 |
| Vitest e2e spec linting | ✅ no errors |
| Code review | ✅ 9.7/10 APPROVE |
| Production code touched | ❌ zero lines |

---

## Why This Matters

**False-green pattern #4.** Cases removal (2026-05-01) was comprehensive on backend + docs, but e2e fixtures lived in isolation — not regenerated from new contracts. Specs ran against dev API in isolation; once bundled, the drift became fatal (422 cascade).

Analogous to infrastructure issue in `false-green-pattern-3rd-occurrence` (WebAppFixture strips IHostedService → seeders never run in tests). Here: e2e specs written against old API shape, bundling exposes 13 cascading gated tests.

---

## Follow-ups (P3, deferred)

1. **PHASE7-TIER-C-FU1-VERIFY**: Runtime Playwright verification post-push. Full stack boot heavy; deferred to user discretion.

2. **Domain stale default** (minor, not blocking): `WorkflowDefinition.cs:31` still hardcodes `EntityType = "Case"`. Now dead code (no `CaseEntityStatusEnumContributor`), but artifact worth a separate cleanup ticket to preserve signal.

3. **Fixture audit post-removal**: When you remove a major module again, add task to audit all e2e fixtures in scope. This catch pattern = 6–8× ROI.

---

## Decisions

- **Bundle both specs in single commit** — same root cause (Cases removal), same remediation pattern, joint reason to land together.
- **Zero production code changes.** Specs are fixtures, not logic. tsc sufficient gate.
- **Deferred runtime verification.** User will push + manually verify Playwright with full stack. Recorded as separate P3 item.

---

## Emotional Reality

Frustrating that removal-side cleanup didn't cascade to fixtures (docs/tests/infra were meticulously audited, but e2e was isolated). Single-line addition (`entityType: 'Generic'`) unblocks 13 tests — proof that small contract misaligns snowball. The lesson is: **removal events need fixture audit pass**, not just code audit.

---

## Unresolved Questions

None. Ready to push after user confirms runtime behavior is restored.
