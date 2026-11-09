# Legacy Stale-Audit: Removed SoD `EnforcementLevel` V4 Compat-Shim

**Date**: 2026-07-10 14:30  
**Severity**: Low  
**Component**: Identity.Application.SoD, Identity.Presentation.Controllers  
**Status**: Resolved  

## What Happened

Performed empirical stale-audit on backlog "Legacy stale-audit" row (22 legacy buckets flagged). INVARIANT 260522-A catch: verified a deprecated compat-shim (`SodRuleDto.EnforcementLevel`, v2.127, 27 releases past 2-cycle removal window) was actually overdue for removal. Executed same-slice.

Removed computed response prop `SodRuleDto.EnforcementLevel => Severity` + `[Obsolete]` annotation + request-side fallback (`?? request.EnforcementLevel`) + suppression pragmas from `ListSodRulesQuery.cs` and `SodRulesAdminController.cs`. Updated docs.

## The Brutal Truth

This was a *clean win* — the kind that feels pointless until you realize it's exactly how technical debt gets actual velocity. The deprecated prop lived rent-free for 27 releases (v2.127 → v2.156) because no user complained. Removing it was low-stakes, but the audit discipline caught it. That's the point of INVARIANT 260522-A: force empirical verification before closing legacy rows, surface the *actionable* backlog row you already own.

The one framing mistake was the backlog row describing a "shadow column `LegacyEnforcementLevel`" that never existed — the compat was the computed property, not a DB column. `git log -S LegacyEnforcementLevel` = 0 adds proved that fast.

## Technical Details

- **Removed files**: 0 (in-place edits only)
- **Modified files**: 2 (`ListSodRulesQuery.cs`, `SodRulesAdminController.cs`)
- **Blast-radius verified**: grep `enforcementLevel` across `src/`, `tests/`, `src/tests/`, `web/src/` = 0 hand-written consumers; FE schema is orval-generated (self-cleans on regen)
- **Build result**: 0W/0E (`-warnaserror`); Identity tests 1,276/1,276 pass
- **Code-review**: APPROVE_WITH_NITS (sole nit: `docs/dependency-map.md:399` documented the removed fallback, corrected same-slice + line 395 re: the never-shipped shadow column)

## What We Tried

No failed attempts — the audit was methodical: (1) read the backlog row verbatim, (2) empirical probe of the SPECIFIC identifier, (3) categorize (never-shipped vs overdue), (4) execute removal + verify blast-radius, (5) update docs.

Also confirmed 2 companion legacy buckets (already swept 06-20/06-23): no new trigger fired, re-affirmed closed.

Updated `BTC-SKIPPED-TESTS-REACTIVATE` in-place: claim "15 skips + Phase-01 unpushed" decayed to 3 Smoke skips only (9 + 3 already un-skipped in `394e2efb`). Effort revised 5–7h → ~2–3h.

## Root Cause Analysis

Compat shims without a hard removal date naturally decay. The v2.127 row said "remove after 2 release cycles" but no enforcement mechanism tracked it — neither a reminder task nor a CI warning. At 27 releases past, the signal was weak: no user error (graceful fallback on `UnmappedMemberHandling` defaults), no test failure (deprecated prop is silent), so it lived.

The deeper lesson: a deprecation window is a *guarantee only if enforced*. INVARIANT 260522-A catches this by *regular* empirical audit (before closing legacy rows), not by reactive cleanup.

## Lessons Learned

1. **`git log -S <identifier>` is the oracle.** Distinguish "shipped-then-stale" (add commit exists, likely cleanup task) from "never-shipped-obsolete" (0 adds, strategy superseded before ship). Saves 20min per row.

2. **Time-boxed deprecation rows are high-ROI stale-audit targets.** "Remove after N release cycles" is *already actionable* by definition — either already done (close) or overdue (this case, 27 releases, clean removal). Trigger: `27 >> 2`.

3. **Removing a compat shim requires blast-radius on BOTH sides + BOTH test roots + generated code.**
   - Request side: grep compat param name across `src/`, `tests/`, `src/tests/` (positional calls can hide uses)
   - Response side: grep compat prop name across `src/`, `tests/`, `web/src` (hand-written vs generated)
   - Graceful degradation (no `UnmappedMemberHandling.Disallow`) de-risks; silent-ignore stale clients
   - Verify orval/generated schema self-cleans on next regen (track by version row in backlog)

4. **Backlog row claims can describe strategies that were *never implemented*.** Re-read the actual row against repo history before trusting "shipped" framing. A missing shadow column in git history is proof it never shipped.

## Next Steps

- BTC-SKIPPED-TESTS-REACTIVATE: remain open, effort revised to 2–3h (3 Smoke skips + Phase-01 already merged)
- No follow-up needed; compat shim fully removed and verified
- Docs (backlog.md, completed.md, project-changelog.md, dependency-map.md) synced same-slice

---

**Status**: DONE  
**Filename written**: `docs/journals/journal-20260710-1430-legacy-stale-audit-sod-enforcementlevel-shim.md`
