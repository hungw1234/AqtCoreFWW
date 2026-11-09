# Feature-Dev Phase Pivot: Park CI, Rank Feature Tracks

**Date**: 2026-06-10 15:25
**Severity**: Medium (strategic re-direction, not a code defect)
**Component**: Backlog, roadmap, session strategy
**Status**: Resolved

## What Happened

Session opened with `/aqt-next` diagnostic: working tree clean, 18 ships today, user fatigue flagged (15:12 elapsed, high-cadence maintenance loop). Recommendation: either EOD wrap or a CI contention-cap slice.

User overrode both. Directive: **"CI chưa quan trọng hãy tạm thời bỏ qua, đây là giai đoạn tập trung phát triển các module, feature quan trọng, làm mịn... Hãy cập nhật để có hướng đi đúng."** (CI not critical right now, this is a feature-dev phase, focus on important modules/features and polish. Update docs for correct direction.)

Recognized: the last ~15 sessions drifted into test/CI/flake maintenance (clone bugfixes, stale-test triage, CI lockfile, e2e orphan leak, fullsuite contention) while product modules sit feature-complete on headline targets (v2.137, 15 BE modules, 375+ endpoints, Workflow 7-phase + 7 resolver modes, BP MVP, QĐ742 40/40). This was drift, not plan.

## The Brutal Truth

Fatigue + low-visibility backlog taxonomy buried the drift. The "T5 Strategic" tier mixes feature-dev work (T5-01 KPI Threshold, T5-06 SDK) with 40d gov-blocked items (P6 remediation, multi-instance redesign). The "T1-T5 by work-type" classification is sensible for prioritization but doesn't tell you WHERE to put effort next. Without a "Current Focus" overlay, the backlog reads as a flat undifferentiated list.

18 ships in one day is unsustainable and unpleasant. This session = course-correct without shipping more maintenance work. The pivot is real: we're feature-starved, not feature-complete.

## Technical Details

**State at Session Start:**
- Backlog: 600+ rows (T1 blockers, T2 test-infra, T3 CI, T4 polish, T5 strategic)
- Roadmap: generic phases; no "next flagship" marked
- 18 commits today: all maintenance (CI, tests, clone, e2e, stale-tests)
- CI status: disabled since 2026-04-13; not in scope for re-enable this phase
- Product: v2.137 stable, headline modules shipped, no open P0s

**Decisions Captured:**
1. **CI / test-infra PARKED** — re-activate trigger is release-gate needed OR dedicated stabilization sprint (future decision). Parked rows: `CI-DISABLED-SINCE-APRIL` (chain-head), `T3-01/02/03`, `R4-EXEC`, `T4-03`, `T5-07`, `RT2-15-PHASE-05-EQ2`.
2. **Feature tracks ranked** — (1) Flagship TBD (candidate: `T5-01` KPI Threshold & Alerting, presale doc exists, ~20d) → (2) Quick-win `T5-06` SDK NuGet (2d, independent) → (3) Workflow/Forms/BP polish → (4) P6-DEFER-1 Forms→Workflow remediation (blocked-on-product-input) → (5) WF-MULTI-INSTANCE-ASSIGNMENT (deferred, schema redesign).
3. **Flagship NOT chosen this session** — deferred to next session (avoid blind 20d commit on fatigue).

**Artifacts Updated (committed-ready):**
- `backlogs/backlog.md`: inserted `🎯 Current Focus — Feature-Dev Phase` block near top (ranked tracks table + Parked index). Annotated "Top open items" line. Marked Stats T3 tier = PARKED.
- `docs/development-roadmap.md`: inserted `🎯 Current Phase Focus` block after title.
- Local plan: `plans/260610-1520-feature-dev-phase-direction/plan.md` (gitignored, direction artifact).

## What We Tried

1. **User decision point:** "CI re-enable vs EOD wrap?" → User override: "Neither; redirect to feature-dev." Then: "Unilateral flagship pick vs user-decides?" → User chose: "Mixed — re-org first, pick flagship next session."
2. **Re-org method:** Physical backlog re-sort (messy, churn-risk) vs annotation overlay at top (DRY, keeps detailed rows). Chose annotation; backlog taxonomy stays stable.
3. **Flagship field selection:** Presale KPI doc not yet read into scope; candidate makes business sense (pre-sales artifact = stakeholder signal) but commitment-risk at high fatigue. User approved deferral; next session decision.

## Root Cause Analysis

Why the drift into maintenance? Each fix-session surfaced an orthogonal blocker (clone stale-tests → CI-disabled discovery → lockfile policy → e2e orphan-leak → fullsuite contention). Each blocker was real and independent. But the *cumulative effect* was a ~15-session loop with zero feature output despite stable product.

This is not a process failure (each decision was correct). It's a visibility failure: no explicit "next feature phase" roadmap meant each session picked the most-urgent blocker without a north-star. Backlog taxonomy by work-type is correct for steady-state but doesn't communicate direction.

## Lessons Learned

1. **Overlay annotations over physical re-sorts on large backlogs.** Re-sort = O(n) churn, rebase conflicts, hidden row moves. Annotation at top = O(1), DRY, preserves row order for detailed investigation. For a 600-row backlog, annotation wins.

2. **Work-type taxonomies (T1-T5) are good for categorizing urgency but bad for communicating next-phase direction.** Backlog needs BOTH: work-type tiers (for priority) + phase-focus block (for strategy). Neither alone is sufficient.

3. **High-cadence maintenance loops are unsustainable and demoralizing.** 18 ships in one day, all stale-test fixes, is a signal that direction needs re-set, not that the team is productive. Ship maintenance work in designated slices, not every session.

4. **Flagship feature pick = user call, not engineer unilateral.** KPI module is presale-driven; deferring to next session and asking "which matters more: KPI vs SDK?" is better than guessing on fatigue.

5. **Honest scope deferral > false-positive finish.** This session did NOT ship feature code. It re-organized backlog + parked CI + surfaced decision points. That's the real value.

## Next Steps

**Immediate (committed, ready to push):**
- Push backlog + roadmap annotations; close `260610-1520` plan locally (direction recorded).

**Next Session (user decides):**
- Read presale KPI Threshold `.docx` into scope.
- Pick flagship feature (T5-01 KPI or alternate).
- Kick off `/ck:plan` for flagship (estimated 20d, multi-phase).
- Execute first phase (likely 2-3d setup + schema).

**CI Re-Enable (future decision):**
- Currently blocked on 3 orthogonal upstream fixes (contention-cap, TenantIsolation scoping, Audit obsolete test).
- Trigger = release-gate needed OR dedicated stabilization sprint (Q2 Q3 planning call).
- Chain-head: `CI-DISABLED-SINCE-APRIL` (backlog).

**Open Questions:**
- Presale KPI Threshold — does it align with market messaging / Q2 roadmap? (User reads next session.)
- SDK NuGet quick-win — should this ship BEFORE flagship (customer-facing) or AFTER (internal infra)? (Depends on presale KPI timeline.)
- Workflow/Forms/BP polish track — what's the highest-ROI polish item? (Audit needed before committing effort.)

---

**Verification:**
- Backlog re-org: M (uncommitted locally, awaiting user 👍)
- Roadmap re-org: M (uncommitted locally, awaiting user 👍)
- CI status: parked, documented, backlog indexed
- Feature direction: explicit, user-decided, next-session pickup
- Artifacts: docs + plan ready for commit once user approves
