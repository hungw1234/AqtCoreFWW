# Journal — P7 Cleanup Batches 1+2 (v2.137p94+p95)

**Date:** 2026-05-22 16:30 ICT
**Session arc:** PHR-V2 P6 closure MERGE (earlier, PR #115 `4af401f2`) → P7 audit (FE-FU role-perm matrix) → P7 Cleanup Batch 1 (close 7 rows) → P7 Cleanup Batch 2 (DEFER-3+7 TDD slice) → wrap.
**Ships today:** 21 commits since midnight (most across PR #115 cluster + 2 direct-to-main cleanup batches).

---

## Session Goal

Close as many PHR-V2 P7 carry-fwd rows as possible without crossing the fatigue floor on a 21-ship day. Started from a fresh `aqt-next` scout that ranked 4 options (cleanup batch / EF-fixture / FE-FU audit / EOD wrap).

## What Worked

- **FE-FU 4-A-A audit (Option C-then-A in scout):** read-only grep audit produced clean result in ~20min; zero FE consumers of `usePostApiV1WorkflowInstances`; BE seeder + FE permission registry already consistent post-PR#114. Audit report: `plans/reports/audit-260522-1530-fe-instance-manage-callers.md` (gitignored).
- **P7 Cleanup Batch 1 bundle (commit `c6d35de5`):** 7 rows closed in one PR-ish commit. Title null-fallback shipped × 2 spots; Singleton flip for `IWorkflowGraphValidator`; 4 doc-only closures (DEFER-5/8/10/11 + FE-FU). WF.App 533/533, WF.Infra 79/79 PASS. Refined scope after scout caught that DEFER-5 was audit-only (no `IsFailed` property exists on `WorkflowInstanceResult`) — saved time vs blind impl.
- **P7 Cleanup Batch 2 TDD slice (commit `8769c224`):** F7r Cancel race test reused `WinnerFirstInterceptor` from same assembly (clean DRY — no helper extraction needed); I8 canonical-uppercase DB-roundtrip mirrored existing I6/I7 seed pattern. WF.Infra 80/80 (+1), WorkflowGraphPublishValidation 4/4 (+1).

## What Didn't / Lessons

- **Scope-of-"all" mismatch caught pre-impl.** User said "P6-DEFER all --tdd"; surface-pass identified 2 of 5 remaining items as undeliverable this session (DEFER-1 = product-owner gate; DEFER-4 = depends on missing BP integration fixture). Trade-off table presented before any code touched — user accepted Option B (DEFER-7 + DEFER-3 only). **Lesson:** "all" claims in late-day requests are anchor-points, not commitments; surface blockers loudly BEFORE planning crystallizes.
- **I8 expected `200 OK` but endpoint returns `204 NoContent`.** Quick fix once integration test failed; could have been caught by reading the controller decorator (`[ProducesResponseType(204)]`) up front. **Lesson:** for integration tests touching unfamiliar endpoints, grep the controller `[ProducesResponseType(N)]` attribute set BEFORE writing assertions.
- **`plans/` gitignored intentionally.** Both the FE audit report (`plans/reports/audit-260522-1530-fe-instance-manage-callers.md`) and the phase-05c doc tidy edit are local-only. Per memory note 260522-G, ops artifacts ship via `docs/ops/` to tracked location; the FE audit is one-time evidence (close-note in tracked backlog references it). For phase-doc edits like DEFER-10, the working-tree change is preserved locally but won't migrate to a fresh clone — backlog close-note captures the decision summary. **Decision:** accept gitignored local artifacts for one-time audits + plan-internal notes; promote to `docs/ops/` only when ops re-runs the artifact.
- **DEFER-2 deliberately not touched.** Test-infra change (IModelCacheKeyFactory discriminator) has blast-radius across many integration test classes. On a 21-ship day past 15:00 local, the risk of subtle break exceeds the upside of test-infra polish. Filed as next-session candidate.

## Decisions Made

1. **Refine `cook` scope before impl when user "all"-claim is ambiguous.** Presented 4-option trade-off (A/B/C/D) for the cleanup batch; same pattern repeated for the TDD batch. User picked option B both times — narrower scope than asked.
2. **Direct-to-main for low-risk cleanup batches.** Both P7 batches shipped without feature branch / PR. Same convention as prior `c6d35de5` precedent: pure cleanup / test-only changes, ≤5 files, all gated tests PASS, low blast-radius. Branch protection didn't reject.
3. **DEFER-8 closed as WONTFIX-cosmetic.** YAGNI: adding `IsNotFound` discriminator to `WorkflowGraphValidationOutcome` would be dead code — current sole caller (`ActivateBusinessProcessCommandHandler`) doesn't branch. Re-open when a caller needs the field.
4. **DEFER-5 closed as audit-only.** `WorkflowInstanceResult` exposes only `IsSuccess` (no `IsFailed`); all 3 callers use canonical `!wfResult.IsSuccess` pattern; `saveResult.IsFailed` in `InProcessClient` is legit DB-failure handler. No code change warranted.

## Test Status

- **WF.App:** 533/533 ✅ (unchanged baseline)
- **WF.Infra:** 80/80 ✅ (+1 F7r from Batch 2)
- **WorkflowGraphPublishValidation:** 4/4 ✅ (+1 I8 from Batch 2)
- **Build:** 0E / 8W (pre-existing NU1903 Scriban — out of scope)

## Next Session

1. **DEFER-2** (T3 P2, ~1h): WebAppFactory EF model-cache contamination fix. Add `IModelCacheKeyFactory` discriminator OR consistent mock behavior across integration test classes sharing `DbContextOptions<T>`. Fresh-context session preferred — test-infra change with broad ripple.
2. **DEFER-1** (P3 strategic, ~1d): `WorkflowLinkPendingEvent` for orphan Forms submissions. Needs product-owner sign-off on retry semantics + UX. Schedule a product gate session.
3. **DEFER-4** (T3 P2, blocked): I3/I4 BP HTTP integration tests. Blocked on missing BP integration test fixture. Either build the fixture (separate plan, ~1d+) or accept that BP HTTP gaps are tested at unit/Application layer only.
4. **PHASE7-FE (P1):** Phase 7 FE Contract Validation Tier B/C/D — blocked by DTO-CONTRACT fixes (older backlog item, predates PHR-V2; verify if still relevant after entity-tier refactor).

## Unresolved Questions

- None for the work shipped. The 3 remaining P7 carry-fwd items are all gated by external/strategic factors (product, test-infra ripple, separate-scope dependency) — not session-blockers.
