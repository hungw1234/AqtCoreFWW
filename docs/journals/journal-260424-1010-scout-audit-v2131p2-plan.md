# Scout + Plan + Scope-Conflict Resolution (v2.131p2 Gap Audit)

**Date:** 2026-04-24 10:10–11:52
**Severity:** Medium (coordination + scope impact, no code loss)
**Component:** Gap audit planning, parallel session coordination
**Status:** Resolved (renamed v2.132 → v2.131p2, scope finalized, 10 gaps scheduled)

## What Happened

Session executed 3 sequential acts: (1) `/scout` bare run scanned 17 BE modules + shared infra, identified 11 gaps (2 P0, 6 P1, 3 P2), health A-/94% production-ready. (2) `/ck:plan --deep` composed 7-file plan for gap cleanup; 2 parallel researchers (VN legal + conventions), brainstormer red-team revealed 3 fatal flaws + 5 major concerns in phase specs. All flaws fixed via targeted Edits. User answered 9 validation questions in 2 AskUserQuestion rounds, expanding Phase 5 scope (+4-5h code work). (3) Mid-finalize `git pull` revealed parallel session had claimed v2.132 label (PR #58 merged 11:38 for auth hardening), causing label collision. Resolution: renamed plan v2.132 → **v2.131p2**, removed ADR-011 (already shipped by PR #57 at 11:03), reduced scope gaps 11 → 10 via 8 targeted Edits. Final plan: 6 phases, 10 gaps, v2.131p2 label, blocks v2.133 async workflow.

## The Brutal Truth

Parallel-session race during finalize was disorienting. Had been heads-down planning for 1.5 hours on v2.132, and mid-verification a `git pull` revealed another session had **already merged the v2.132 label to main** (PR #58, auth hardening). That moment of "oh no, collision" hit hard. Pragmatic fix (rename to v2.131p2, remove duplicate ADR) kept momentum, but exposed real risk: gitignored `plans/` directory means scope conflicts happen invisibly. No merge conflict signal, no PR review catch — just two humans independently picking the same version number. Pull early and often during planning is now a hard lesson, not advice.

Red-team brainstormer save was massive and emotionally validating — caught 3 catastrophic implementation risks (overwriting 154 lines of working test code, wrong test project name, phantom pragma for nonexistent call site). That's 6–8 hours of thrashing avoided. ROI on spawn-brainstormer before planner finalize is absolutely worth it.

## Technical Details

**Scout findings (master report):**
- 17 BE modules scanned, 4 parallel Explore agents
- 11 gaps identified: Gap #1 (Integration.Presentation 404), Gap #2 (Search empty dirs), Gaps #3–5 (SystemParams CQRS + MediatR), Gap #6 (MasterData KBNN/currency), Gap #7 (Signature CrlRefreshJob cosmetic), Gap #8 (Search CQRS docs), Gap #9 (Signature 50% coverage), Gap #10 (Identity 10 events), Gap #11 (SystemParams cache edge tests)
- Verdict: 94% production-ready, no active dead-pipes, AI deferred state verified clean
- Scout report: `plans/reports/scout-260424-1010-gap-audit-master.md`

**Red-team fatal flaws (3):**
- **FF-1 (Phase 6):** SystemParamServiceCacheTests.cs already exists (154 lines, 6 methods); phase assumes NEW work, would overwrite or create duplicate
- **FF-2 (Phase 3):** Plan hardcodes test project as `AqtCoreFW.SystemParams.Application.Tests` (nonexistent); reality is `AqtCoreFW.SystemParams.Tests` (existing, correct)
- **FF-3 (Phase 2):** Plan assumes CrlRefreshJob has Hangfire registration site (grep confirms zero call sites, dead code); pragma step meaningless

**Fixes applied (8 Edits):**
- Phase 6 rescoped to EDGE cases only (TTL expiry, cache stampede, cross-tenant isolation); effort reduced 1h → 30m
- Phase 3 test paths corrected to existing project; removed create-csproj contingency
- Phase 2 reframed as DELETE CrlRefreshJob.cs (Option A, preferred per red-team); pragma removed from success criteria
- Plan.md description updated; effort for Phase 5 expanded 2h → 5-5.5h (added HMAC Hangfire job + Reporting data-API wrapper per user validation decisions)
- ADR count corrected: 6 total (006–010, not 006–011); ADR-011 removed (Rules baseline already shipped by PR #57)
- Success criteria #2 (grep pragma) removed (Phase 2 now DELETE, not annotate)
- Cross-plan frontmatter applied: v2.131p2 `blocks: [260424-0922-v2133-async-workflow-publish]`

**Parallel session collision (git pull @ 11:20):**
- PR #57 merged 11:03 (Rules deferred formalization, commit f49bbeee) — removed ADR-011 from scope, no conflict
- PR #58 merged 11:38 (v2.132 auth hardening plan journal, commit 635acdb7) — **claimed v2.132 label**, collision with my plan
- Resolution: renamed my plan v2.131p2 (patch semantics, fits gap-cleanup as housekeeping before feature work)

**Validation decisions resolved (9 → 10 gaps):**
| Gap | Question | Decision | Effort impact |
|-----|----------|----------|---|
| #1 | Integration.Presentation 404 | Fix now (Phase 1) | +30m |
| #2 | Search empty dirs | Delete (Phase 1) | +30m |
| #3–4 | SystemParams CQRS | Option A all-in-one, 3 controllers (Phase 3) | +3–4h |
| #6 | MasterData KBNN/currency | DEFER (Phase 5 ADR-006) | legal research confirmed no mandate |
| #7 | Signature CrlRefreshJob | Delete (Phase 2 Option A) | +30m |
| #9 | Signature test coverage | 2 command handler tests (Phase 4) | +1–1.5h |
| #10 | Identity 10 events | Intentional integration surface (Phase 5 ADR-007) | docs only |
| User Q6 | Audit HMAC verification | ADD nightly Hangfire job 02:00 AM (Phase 5) | +2h code |
| User Q8 | Reporting microservice | Plan refactor + IOrganizationClient wrapper (Phase 5) | +2–3h code |
| #11 / PR #57 | Rules baseline | Already shipped (removed from scope) | —0h |

Phase 5 scope expanded from docs-only to docs + 2 code subtasks (+4–5h total).

## What We Tried

1. **Scout alone:** 4 parallel Explore agents, identified 11 gaps, 94% health verdict — worked as designed
2. **Plan --deep:** planner subagent composed 7 files; cross-plan frontmatter `blocks`/`blockedBy` applied to v2.133 — worked
3. **Red-team brainstorm:** spawned on phase files, found 3 FF + 5 MC — ROI massive
4. **AskUserQuestion rounds:** 2 passes, 9 validation questions answered, scope expanded — worked, user committed to +4–5h effort
5. **git pull mid-finalize:** caught PR #57 (good, no conflict) + PR #58 (collision, resolved via rename + ADR removal)

## Root Cause Analysis

**Scope collision (v2.132 race):**
- Root: gitignored `plans/` directory = session-local artifacts, no cross-session merge conflict detection
- Two independent planning sessions picked v2.132 label without visibility into each other's work
- PR merge (PR #58 @ 11:38) happened before my finalize (11:20 local time, but git log not checked until 11:20 pull)
- Lesson: **pull early in planning**, not just at finalize. Check `git log origin/main` + search `docs/journals` for version strings BEFORE committing plan frontmatter version label

**Red-team fatal flaws (3):**
- Root: planner read existing phase code but did NOT verify against actual codebase before drafting Success Criteria. Plan author assumed test project name + Hangfire registration + cache test scenarios without Grep/Read validation
- Red-team explicitly verified via grep — found zero call sites for CrlRefreshJob, zero cross-references for Phase 6 scenarios
- Lesson: **planner MUST verify code-facing Success Criteria with actual codebase** (Read + Grep), not assume knowledge. Red-team spawn is cheap validation insurance

**YAGNI validated:**
- VN legal researcher confirmed no legal mandate for KBNN + currency generics (Circular 185/2015 + 324/2016 scope specific, not broad). Defer safe, not risky

## Lessons Learned

1. **Parallel planning is a real coordination problem**, not theoretical. Two sessions independently picking v2.132 without merge-conflict visibility. Mitigation: (a) check `git log origin/main` early in plan composition, search `docs/journals` + plan dir for target version strings; (b) cross-plan frontmatter `blocks`/`blockedBy` helps (would have caught if pre-existing v2.132 plan had it), but only if established before collision. For future: consider "reserve version label" commit to main before multi-hour planning, or use session-unique plan dir naming (e.g., `260424-1040-...` with timestamp precision).

2. **Red-team before implementation = 6–8× ROI on catch rate.** FF-1 alone (overwriting 154 lines of working test code) would have stalled implementer for hours. Recommend: always spawn brainstormer on phase plans with >2h effort or >3 code files touched.

3. **Cross-plan frontmatter works.** Applied `blocks: [slug]` bidirectional update successfully. Useful for v2.133 (now blocked by v2.131p2 cleanup). Pattern: use this for parallel plan coordination going forward.

4. **Code-facing Success Criteria MUST be verified against codebase.** Planner intuition ≠ reality (test project name, phantom call site, existing test coverage). Require Grep/Read on SC before finalizing phase.

5. **YAGNI > speculation.** Research > brainstorm. VN legal researcher solved 2h of theoretical debate in 20 minutes (KBNN/currency is NOT a legal mandate, deferral safe).

6. **Scope expands when users make decisions** (Q6 + Q8). Plan Phase 5 defensively; user decisions always add features. Buffer accepted.

## Next Steps

1. **Finalize cleanup (docs-manager call due):** Update 6 tracked artifacts (dependency-map.md, project-roadmap.md, project-changelog.md, backlogs/backlog.md, memory/project-state.md, memory/MEMORY.md) with v2.131p2 label + 10-gap status (shipped 2026-04-24, next session implement Phase 1 P0s)
2. **Implementer prep:** Phase 1 (P0 wire + search cleanup) = 30m, no red-team risk, ship first to unblock Phase 3–6
3. **v2.133 async workflow plan:** already exists (`plans/260424-0922-v2133-async-workflow-publish/plan.md`), now unblocked once v2.131p2 shipped
4. **Parallel session follow-up:** Notify auth-hardening session (v2.132) that their work noted, zero conflict, v2.131p2 patches before v2.132. Cross-session context: PR #57 rules formalization already upstream, no ADR work overlap.

## Emotional Note

Session rhythm was satisfying: scout revealed real problems, brainstormer caught hidden traps, user made bold scope decisions. Then collision mid-finalize deflated that for ~5 minutes. But pragmatic rename + deconfliction kept the cycle clean. Learned: parallel sessions are coming — need better cross-session visibility. gitignored plans dir is convenient for local iteration, but it's a coordination blind spot when multiple humans plan in parallel. Will propose to user: lightweight version-label reservation protocol or session-unique naming. For now, pull early, verify version strings, apply cross-plan frontmatter proactively.

## Files Touched

**Created (gitignored, local only):**
- `plans/260424-1040-gap-audit-cleanup-v2132/plan.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/phase-01-p0-integration-wire-search-cleanup.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/phase-02-obsolete-marker-search-docs.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/phase-03-systemparams-cqrs-refactor.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/phase-04-signature-test-coverage.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/phase-05-adr-tech-debt-docs.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/phase-06-systemparams-cache-tests.md`
- `plans/reports/scout-260424-1010-gap-audit-master.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/reports/red-team-260424-1040-plan-review.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/research/researcher-260424-1040-vn-masterdata-legal.md`
- `plans/260424-1040-gap-audit-cleanup-v2132/research/researcher-260424-1040-internal-conventions-audit.md`

**Modified (tracked, pending git commit):**
- `docs/dependency-map.md` (by docs-manager, post-session)
- `docs/project-roadmap.md` (by docs-manager, post-session)
- `docs/project-changelog.md` (by docs-manager, post-session)
- `backlogs/backlog.md` (by docs-manager, post-session)
- `memory/project-state.md` (by docs-manager, post-session)
- `memory/MEMORY.md` (by docs-manager, post-session)

**Pulled from origin (pre-merge with local):**
- PR #57 `f49bbeee` (Rules deferred formalization, 2026-04-24 11:03) — upstream ADR-011, zero conflict
- PR #58 `635acdb7` (v2.132 auth hardening plan journal, 2026-04-24 11:38) — v2.132 label collision, resolved rename

## References

- Scout master: `plans/reports/scout-260424-1010-gap-audit-master.md`
- Parallel v2.133 plan (blocked by this plan): `plans/260424-0922-v2133-async-workflow-publish/plan.md`
- Red-team review: `plans/260424-1040-gap-audit-cleanup-v2132/reports/red-team-260424-1040-plan-review.md`
- Parallel session auth journal: `docs/journals/journal-260424-1103-v2-132-auth-hardening-plan.md`
- PR #57 rules deferred journal: `docs/journals/260424-1054-rules-module-deferred-formalization.md`
