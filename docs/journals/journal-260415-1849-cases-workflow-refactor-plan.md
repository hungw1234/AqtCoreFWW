# Journal — 2026-04-15 Cases Workflow Refactor Plan Session

**Date:** 2026-04-15 afternoon/evening
**Nature:** Planning-only (zero code). Architectural decision + red-team validation.
**Outcome:** APPROVED — ready to implement Release A (phase-00) next session

---

## What Happened

Revised existing refactor plan (`plans/260415-1737-cases-workflow-refactor/`) to adopt **Option B (hybrid masterdata taxonomy)** for workflow state/action codes. Added new `phase-00-workflow-masterdata-taxonomy.md` (two new entities: `WorkflowStateTaxonomy` + `WorkflowActionTaxonomy` as shared dictionary with soft FK, copy semantics). User provided 4 defaults (AuditableEntity, Full CRUD, TTL 30min, `/masterdata` namespace). Scout discovered zero-cost infrastructure wins: `AuditableEntity<Guid>`, `AuditBehavior<,>`, `IDistributedCache` all exist in codebase already.

**Cache architecture debate:** User asked "tại sao không dùng Redis làm distributed cache?" — forced useful re-evaluation. Explored 3 options (two-tier L1+L2, pub-sub invalidation, Redis-only). User chose **Option C (Redis-only)** — zero L1 staleness, simpler, matches taxonomy's read-rare profile.

**Red-team validation:** Two independent review rounds caught **12 critical/significant issues** that planner self-review missed. Phase-00 effort bumped 48h → 54h; total plan 18h → 72h. User validation interview confirmed all 4 recommended options.

---

## The Brutal Truth

**Planner round 1 missed 3 critical bugs:**
1. **Seeder reversion:** INSERT queries could resurrect admin-deprecated rows on redeploy (fixed: INSERT-IF-NOT-EXISTS)
2. **Concurrency token confusion:** Spec'd `Version int` display-counter instead of `[Timestamp] RowVersion byte[]` for optimistic locking
3. **Permission scope:** Tenant admin could deprecate global workflow codes (fixed: `PLATFORM.*` permission tier for SystemAdmin only)

This stings because the planner had full context and still conflated two unrelated concerns (display version ≠ concurrency token). Self-review bias is real and dangerous — independent reviewer with cold context found issues planner never questioned.

**Two-tier cache felt natural** until user asked the obvious question. "Why not just Redis?" forced simplification. Sometimes the simplest architecture is the right one, and team debate is how you surface it.

---

## What We Tried

### Round 1: Planner self-review
- 7 risk sections drafted, soft FK pattern validated
- Missed 3 critical + 6 significant issues
- Plan converged locally but failed external scrutiny

### Round 2: Code-reviewer (independent, cold)
- **Critical (3):** seeder reversion, concurrency token, permission scope (all detailed above)
- **Significant (6):** ClassificationLevel consistency, missing state/action enums in migration, tenant IsActive default, cache key namespace collision, missing cache invalidation on soft delete, cascading delete risk
- **Minor (3):** README grammar, enum ordering, missing XML comments
- Applied all 12 fixes, bumped effort 48h → 54h

### Round 3: Verification-only (15 min)
- Checked all 12 fixes in context
- Validated no regressions, no new risks
- 12/12 PASS → APPROVED

### User validation interview
- Confirmed V1 (standalone release, split Release A + Release B)
- Confirmed V2 (`PLATFORM.MASTERDATA.WORKFLOW_TAXONOMY.MANAGE` permission naming)
- Confirmed V3 (ClassificationLevel=Public)
- Confirmed V4 (comprehensive 9-state + 9-action seed)

---

## Why It Hurt

- 3 red-team rounds feels excessive until you realize round 1 shipped blind (3 critical vulns). Planner confidence was unearned.
- "Version int" mistake is embarrassing — conflating display version with concurrency control is Basic ORM Mistake. Reviewer caught it cold; planner didn't.
- 48h→54h bump (6-hour slip) happened because round 2 exposed underestimated seeding complexity and cache migration scope.

**The real kick:** we were _seconds_ away from writing phase-01 with those bugs baked in. Round 2 reviewer paid off massively.

---

## Lessons Learned

1. **Scout infra first, plan around it.** `AuditBehavior<,>` and `IDistributedCache` already in codebase saved us from spec'ing new infrastructure. Would have been 20+ hours of unnecessary work. Always verify tool/pattern existence before designing around them.

2. **Independent review > self-review.** Planner with full context still missed 3 critical issues. Cold reviewer (no attachment to original plan) found them in 45 minutes. Budget independent review into planning workflows.

3. **Simple question forces simple architecture.** User's "tại sao không dùng Redis?" triggered reconvergence on Redis-only (vs. two-tier L1+L2). Simplicity won. Lesson: obvious questions often expose over-engineered designs.

4. **Concurrency tokens are not display versions.** `Version int` (display counter) ≠ `[Timestamp] byte[]` (optimistic lock). Know the difference cold, or unit-test-drive the pattern explicitly.

5. **Effort estimates are fragile.** 48h→54h (12.5% slip) from two-tier cache + seeder INSERT-IF-NOT-EXISTS complexity. Red-team forces honest estimation.

---

## Next Session

Ready to implement Release A (phase-00).

**Recommended approach:**
```
/clear
/ck:cook --auto plans/260415-1737-cases-workflow-refactor/phase-00-workflow-masterdata-taxonomy.md
```

Key artifacts (all local in `plans/`, gitignored):
- `plans/260415-1737-cases-workflow-refactor/phase-00-workflow-masterdata-taxonomy.md` (~340 lines, all revisions applied)
- `plans/260415-1737-cases-workflow-refactor/plan.md` (18h → 72h, §9 Round-2 Revisions, Release A/B split documented)
- `plans/reports/red-team-round2-260415-1814-workflow-taxonomy.md` (12 findings + fixes)
- `plans/reports/red-team-round3-verify-260415-1836-workflow-taxonomy.md` (verification pass)

---

## Key Infrastructure Discoveries

For future planning:
- **`AuditableEntity<Guid>` + `AuditBehavior<,>`** — fully integrated in codebase. Use pattern without hesitation.
- **`IDistributedCache`** — wired to Redis via StackExchange.Redis. No setup required; just inject.
- **Soft FK pattern** (copy semantics + version check) — documented in Organization module. Safe architectural pattern for masterdata.

---

## Status: DONE

Plan approved, zero code changes, ready for implementation phase.
