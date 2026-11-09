# PHR-V2 Phase 6 Closure — Tests & Docs (v2.137p93, 2026-05-22)

**PR:** [#115](https://github.com/thiennd212/AqtCoreFW/pull/115) | **Branch:** `feat/phase-06-tests-docs-closure` | **Commits:** 10 | **Verdict:** APPROVE-WITH-NITS 9.0/10 (folds H1/M1/M2 in-branch)

## What shipped

Final phase of plan `260519-0421` (Process-Handler-Resolution v2). 5 ratified sub-slices + post-review folds:

| Slice | Surface | Outcome |
|-------|---------|---------|
| S1 docs | `code-standards.md` +3 INVARIANT subsections (4-A-A / 4-B-A / 4-C-A) + audit kit promoted from gitignored `plans/reports/` to tracked `docs/ops/phase-05b-empty-pool-preflight-audit.md` + Q5 mixed-mode caveat + (fold) Q6 orphan-FK pre-flight | doc-only |
| S2 DRY | `WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync` — 22 LOC × 5 callers consolidated | build 0E |
| S3 anti-tautology | `CreateInstance_PrincipalWithAllPermsExceptInstanceManage_Returns403` — 260522-E necessary-vs-sufficient lock | build 0E |
| S5 RowVersion sweep (GATE-2 Path A) | new `WorkflowTaskRowVersionRegressionSweepTests.cs` — 7 facts close RT2-12 at infra level | 7/7 PASS, 5s |
| S4 nav-prop migration (GATE-1 Path A) | Closes **5b-A PERMANENT INVARIANT at root** — WorkflowTask.Instance nav-prop + HasOne.WithMany.HasForeignKey + new EF migration + drop TX + single SaveChanges via IDENTITY propagation | App 533/533 + Infra 79/79 + I1+I2 2/2 |
| S6 backlog | Phase 7 carry-fwd section — 11 P6-DEFER rows | doc-only |

Folds (post code-review): **H1** Q6 orphan-FK pre-flight in audit kit; **M1** code-standards example aligned with actual `WorkflowTaskRepository.cs:140-143` (tenant predicate omitted); **M2** origin citation corrected.

## What worked

1. **User gate re-negotiation after scout.** Initial S4 effort estimate was 2h based on planner spec; scout revealed planner missed Domain nav-prop + factory signature change. Re-gating with user (3 signals: §D recommends Path B, DCAND-a precedent, INVARIANT 5) honored consent — they chose to push through Path A with eyes open. Outcome clean (Infra 79/79 + Integration I1+I2 2/2).

2. **Advisor mid-slice bug catch.** S5 was committed with column name `ModifiedAt` that does not exist on the schema (correct is `UpdatedAt` on base Entity). Advisor surfaced this as one of three pre-S4 signals before tests were actually run. Fix landed as commit `2a7530fe`; subsequent run = 7/7 PASS. Without the advisor call, the failure would have manifested at finalize-test time after 2+ hours of S4 work.

3. **4-A-B fold-now norm continues paying off.** 3 code-review findings (H1/M1/M2) folded in-branch (~10 min) rather than punted. Each was bounded, same surface, and removed deploy-risk surface (H1 prevents `SqlException 547` in non-clean envs).

## What did not work / lessons (3 NEW META-L candidates)

- **260522-F — advisor-driven mid-slice fix discipline.** S5's `ModifiedAt` SQL was silently broken; build-clean did not catch. PERMANENT INVARIANT: when bumping rowversion via raw SQL, GREP the entity (`*.cs`) AND base class (`SharedKernel/Domain/Entity.cs`) for column existence FIRST. Reference test `ClaimGuardExceptionDiscriminationTests` bumps known columns (`Status`, `AssigneeId`); future RowVersion-bump test scaffolding should follow that pattern.

- **260522-G — gitignored ops artifacts.** `plans/reports/` is gitignored, so a backlog claim "audit kit shipped" was misleading until the file was promoted to `docs/ops/`. INVARIANT: ops artifacts that will be consumed by ops engineers MUST ship to a tracked location (`docs/ops/` or `docs/runbooks/`); the changelog claim and the file's git-tracked state must align. Honored mid-finalize via commit `4896e644` + changelog amend.

- **260522-H — S4 stacking ratified-with-eyes-open.** DCAND-a precedent says schema-change ships in own slice + branch + PR. User opted to stack S4 inside the Phase 6 closure PR after surfacing 3 signals (§D recommends B, DCAND-a precedent, INVARIANT 5). Outcome clean. Pattern survives when: (a) user gate explicitly re-ratified after new constraints, (b) integration tests verify byte-equivalence post-refactor, (c) advisor available for cross-checks. The precedent is not absolute — surface + ratify + verify works.

## Tests

- WF.App 533/533 (+1 anti-tautology, 5 mock signatures updated for S4 + A6 rewritten)
- WF.Infra 79/79 (+7 RowVersion sweep facts via Testcontainers SQL Server)
- Integration `WorkflowInstancesCreateFirstTaskTests` I1+I2 2/2 (5b critical path preserved post-S4 refactor)
- Architecture.Tests 101/101 (no rule violation introduced)
- Build 0E/0W (8 pre-existing NU1903 Scriban warnings)

## Carry-forward (P7)

11 DEFER items filed under `backlogs/backlog.md` § Phase 7 Carry-Forward (P6-DEFER-1..11). Highlights:
- P6-DEFER-1 5b-L2 `WorkflowLinkPendingEvent` (T3 strategic, ~1d) — new feature design
- P6-DEFER-2 4C-1 WebAppFactory EF model-cache fix (T3 P2, ~1h)
- P6-DEFER-4 5b-M2 BP HTTP integration I3/I4 (T3 P2, dep-gated on BP fixture)

Other P6-DEFER items are cosmetic / observational.

## Deploy note

Run **Q6** from `docs/ops/phase-05b-empty-pool-preflight-audit.md` against every non-clean target environment BEFORE applying migration `20260522063712_AddWorkflowTaskInstanceNavProp`. Orphan rows (`WorkflowInstanceId = 0` from pre-5b code paths) will block migration apply with `SqlException 547`. Audit-kit §Q6 remediation block has the SQL + procedure.

## Plan status

**PHR-V2 = 6/6 phases code-complete** (5/6 already merged to `main`; P6 awaits PR #115 review + merge to close plan `260519-0421`).
