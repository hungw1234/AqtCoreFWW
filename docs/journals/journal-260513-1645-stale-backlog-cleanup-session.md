# Journal — Stale Backlog Cleanup Session

**Date:** 2026-05-13 16:10 → 16:45 ICT
**Branch:** `main` (clean, no commits)
**Session type:** docs-only metadata cleanup (no ship, no code)

## Session goal

User asked "what to do next?" → recommend Tier 2.1 (RBAC Menus, 4-6h). User picked `--auto` to start. Before implementing, premise check found Tier 2.1 already shipped → cascaded into systematic backlog refresh.

## What worked

- **Pre-cook premise verification.** Before implementing each of Tier 2.1/2.2/2.3 + 3.1, ran source-of-truth checks (`MenuService.BuildAndPruneTree`, `useTranslation` ref counts, `[Authorize(Policy)]` grep, FE feature dir listing). All 4 rows already shipped; saved ~7-9h speculative re-implementation.
- **Advisor consultation at premise inflection point.** Caught that "API-driven menu prod gap" reduces to "fallback works as designed"; YAGNI-close not ship. Saved ~1-6h on Tier 2.1.
- **Source-of-truth split surfaced.** `backlogs/backlog.md` (root, living, ~99%+) is authoritative; `plans/reports/backlog-260411-1000-...` is frozen 2026-04-11 snapshot. Documented in refresh footer + memory.

## What didn't / lessons

- **Picked stale doc as source-of-truth at session start.** Wasted ~5min context-loading from the 2026-04-11 snapshot before realizing `backlogs/backlog.md` was the live one. Next time: prefer `backlogs/backlog.md` first; treat `plans/reports/backlog-*` as archaeology.
- **Backlog drift quantified:** ≈1 row per 2 days. 4 STALE closures from a single 11-row snapshot doc in 35 minutes. Snapshots should be archived after 2 weeks or marked `[ARCHIVED]` in title.

## Decisions made

- **Tier 2.1 RBAC-Menus** → YAGNI-OBSOLETE (FE fallback in `sidebar-menu.tsx` permission-gated; no prod admin needs runtime menu edits without redeploy per user confirmation via `AskUserQuestion`).
- **Tier 2.2 RBAC FE i18n** → STALE-BACKLOG (shipped 2026-04-11 commit `72644649`; 0 VN strings, 72 `t()` refs verified across 3 files).
- **Tier 2.3 FORMS.SUBMIT.APPROVE** → STALE-BACKLOG (`[Authorize(Policy = PermCodes.Forms.Submit.Approve)]` on `FormSubmissionsExtController.cs:23` + `FormSubmissionsController.cs:95`).
- **Tier 3.1 FE↔BE Gap G1-G12** → STALE-BACKLOG (plan `260330-1119` archived; all 11 FE features verified present: `consent/`, `rtbf/`, `parallel-branch-status-panel.tsx`, `credential-policies/`, `groups/`, `api-keys/`, `abac-rules/`, `network-policies/`, `backup-policy/`, `mfa-admin/`, `audit-policy/`).

## Test status

- No tests run this session (no code changes).
- Build state inherited from prior session: 0E.

## Files touched

- `plans/reports/backlog-260411-1000-comprehensive-project-backlog.md` (gitignored snapshot annotated; cannot commit by policy — matches pattern of 5 prior YAGNI-closure sessions this week).
- Auto-memory: 3 new entries (`rbac-menus-tier21-yagni-closure-260513`, `rbac-fe-i18n-tier22-stale-backlog-260513`, `backlog-refresh-260411-snapshot-260513`) + `MEMORY.md` index updates.

## Next session priorities

Refreshed R1-R7 list lives in `plans/reports/backlog-260411-1000-...md` "REFRESHED PRIORITY VIEW" + `memory/backlog-refresh-260411-snapshot-260513.md`. Top picks:

1. **R1 Docker CI** (GH #21, open since 2026-03-31, 1d) — highest blast radius
2. **R4 E2E flaky re-baseline** (0.5d) — cheap orientation; may reveal R1 dependency
3. **R7 QD742-FU5 race CAS** on `ConsecutiveLockoutCount` — small, scoped, concrete bug location

Defer Tier 5 strategic items until current cycle ships.

## Unresolved questions

1. Physically archive `plans/reports/backlog-260411-1000-...` to `plans/archive/` given gitignored + replaced?
2. R3 (E2E P1 TCs, plan `260406-1740` in-progress) — still strategically needed?
3. R4 re-baseline scope — quick triage or full Playwright sweep?
