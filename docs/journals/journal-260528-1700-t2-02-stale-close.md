# Journal — T2-02 test-plan phases 4-6 stale close (docs-only)

**Date:** 2026-05-28 ~17:00 ICT  
**Commit:** `501ecce4` "docs(backlog,roadmap): close stale T2-02 — test-plan Phases 4-6 already shipped-scattered" — pushed origin + bitbucket

## Session goal

Close `/cook T2-02` (backlog row: "Test Plan Phases 4-6" claimed 6d epic with 3 PLANNED phases @ 2d each). Applied INVARIANT 260522-A (audit-at-slice-start before trusting old backlog rows) and found entire row stale.

## What happened

**Initial claim:** 6-day epic spanning Phase 4 (Contract/regression 2d PLANNED), Phase 5 (Security 2d PLANNED), Phase 6 (Performance 2d PLANNED).

**Empirical audit (260522-A):** Grepped trait counts + verified file existence. Found roadmap internally contradictory — `docs/development-roadmap.md` line 283 said "Phase 18 v2.13: Comprehensive test plan Phase 4 ✓ Complete" but framework-section (line 395) still "Phase 4 ... PLANNED". Spawned Explore agent for independent census, then verified counts myself:

- **Phase 4 (Contract/regression):** 38 `[Trait("Category","Contract")]` test methods / 15 files in `tests/contracts/AqtCoreFW.Tests.Contracts/` (9 API-envelope + 6 domain-event-schema). Shipped v2.13. ✓ exact match to Explore count.
- **Phase 5 (Security):** 53 `[Trait("Category","Security")]` test methods / 8 files in `tests/integration/AqtCoreFW.Security.Tests/` + 49 compliance tests under `tests/architecture/.../Compliance/`. ✓ exact (53).
- **Phase 6 (Performance):** 21 k6 scripts in `tests/performance/` + `run-all.sh` + `docs/slo-definitions.md` (p95 200/400ms). ✓ via ls (21 .js).
- Dead reference: `Tieu_chuan_Test_He_thong.md` (roadmap line 377) does NOT exist in repo (removed, zero call-sites).

**Verdict:** T2-02 STALE — all 3 phases shipped-scattered across v2.13/v2.15; row never updated post-ship. Residual gaps all defensible YAGNI: no explicit SQLi/XSS test (EF parameterized + ASP.NET/React auto-escape), no crypto/hashing test (.NET BCL), no API-version backward-compat test (no versioning in scope), perf results dir near-empty. Genuinely-greenfield work = comprehensive-test-plan **Phases 7-10** (Reliability, Accessibility WCAG 2.2 AA, Installation/Migration, CI/CD gates) — NOT this row.

**User gate:** Presented findings via AskUserQuestion (backlog/roadmap state + evidence links); user chose "Close stale + fix roadmap" → "Commit + push origin+bitbucket".

## Changes (2 files, docs-only)

- **backlogs/backlog.md:** Closed T2-02 with strikethrough + evidence-cited CLOSED note; updated "Top-open-items" count; tier table T2 count 1→0; total open 22→21.
- **docs/development-roadmap.md:** Fixed framework-section Phases 4/5/6 status PLANNED→COMPLETE with actual test paths (Trait counts + files); corrected header "Phases 1-3"→"Phases 1-6 complete"; removed dead spec reference.

Per rule 260527-K (docs-only cook skips code-reviewer/tester), substitute gate: empirical trait-count grep + `git diff --check` (clean) + source-citation (file:line).

## Why this matters (lessons)

1. **(Reinforces 260522-A)** A 6-day "epic" backlog row can be 100% stale — the tell was roadmap ship-log (line 283) vs framework-section (line 395) contradiction. Empirical census (grep trait-attrs + ls k6 scripts) settled it in minutes vs a 6-day mis-scope. Discipline: ALWAYS verify a stale-feeling row by grepping the identifier before planning.

2. **(Reinforces 260527-F)** Verified Explore agent's census counts myself (grep `[Trait("Category","*")]` + ls k6) post-run — counts matched exactly (38/53/21). The discipline of independent verification is the point, not the paranoia.

3. **Roadmap drift is an active misinformation source** — a "PLANNED" framework section that never updates post-ship spawns phantom backlog rows and compounds audit work downstream. Routine sweep needed (weekly link-check + ship-log consistency scan).

## Next session

- Phases 7-10 planning (Reliability, A11y, Installation, CI/CD gates) — genuine greenfield, not carry-forward cargo.
- Optional: reconcile perf results dir (mostly empty; SLO definitions exist but no historical runs).
