# Journal — Backlog Optimization Archive Pass

**Date:** 2026-06-22 17:17 ICT · **Commit:** `f1e85219` (pushed origin + bitbucket) · **Branch:** main

## Session goal
After `/aqt-next` flagged the backlog at 625L (>600 optimize threshold) on an 8-ship fatigue-day, ran `/cook "Backlog optimization"` — the small, low-risk, fatigue-appropriate option over opening a new feature slice (T5-01 flagship / T5-06 SDK left for a fresh session).

## What worked (verified)
- **24 closed rows archived** → `completed.md §2026-06-22` (06-10→06-21): arch-migrate-guard · USB-DocFlow E2E ×5 · Polish B-E ×6 · Security red-team ×7 · legacy T4-sweep ×5.
- **Header line-3 condensed 12KB→965B** (15 prior-slice ship blocks → latest 2 + changelog pointer); state-line refreshed to Presentation-remediation 06-22.
- **2-tier GATE passed**: (1) ID-level — 24 moved-ID all present in `completed.md`, 0 still a row-header in `backlog.md`, 20 KEEP/open-ID re-grepped present; (2) header-diff HEAD↔working — exactly §06-21 (`##`) + 8 closed `###` rows removed, 0 collateral.
- backlog.md 625→563 lines; bytes 154.7KB→107.6KB (−30%).

## What didn't / lessons
- **Python/Edit interleaving desyncs harness file-state.** After a `python` write spliced line-3, the next `Edit` failed "file modified since read" → needed a re-Read. Lesson: don't interleave external-process writes with `Edit` on the same file mid-sequence; pick one mutation channel per phase.
- **Pivoted bulk deletion to a single Python line-range filter pass** (drop lines whose 1-based index ∈ kill-set, computed against stable original numbering since the line-3 swap kept line-count). Deterministic + atomic + avoids embedding 12KB of backtick/unicode-heavy text into 24 per-block `Edit` calls (which desync-cascade and risk match failures). Sanity-asserted anchors + keepers inside the script before writing.
- **Line-count is the wrong bloat metric here.** The real win was line-3 (a single 12KB physical line) → `wc -l` barely moved (−62) while bytes dropped 30%. Measure bytes when one line carries the bulk.

## Decisions made
- **Skipped the `code-reviewer` subagent** for a docs-only archival — the substantive review (no open row lost, no contract broken) is fully covered by the ID-GATE + header-diff, and a markdown archival has no code/test/lint/contract surface. Proportionate per YAGNI/token-efficiency; flagged the skip transparently.
- **Archive density = 1-3 lines/row** (ID + verdict + commit/verify), not full paragraphs — full narrative already lives in changelog/journals/git. 24 rows cost only +41 lines in `completed.md`, stays grep-able.
- 🔴 **INVARIANT 260522-A**: kept `VIETTEL-PENDINGTX-USERID-FROM-CCCD` verbatim — ✅ FIXED only Phase 01 (generic); active plan P02-P04 + go-live gate Viettel `callbackUrl` UNVERIFIED. Partial-closed ≠ archivable.
- Tier counts (T1-T5/P6) left unchanged — the 24 rows were already ✅ CLOSED (not in the active tally); archiving moved bytes, not counts.

## Test status
n/a (docs-only, 0 code). Verification = 2-tier GATE (pass) + header-diff (exact-match intent).

## Next session
- Flagship **T5-01 KPI Threshold & Alerting** (re-scoped ~8-10d on existing alerting infra — scope via `/ck:plan` first) OR **T5-06 SDK NuGet** warm-up (de-flagged false-ready → needs scoping, probe "0 prior work" claim per INVARIANT 260522-A before opening).
- **Deferred FU**: ~26 closed-inline rows still padding the 22 legacy `##` buckets (`backlog.md` 215-389) — a dedicated stale-audit/archive pass, out-of-scope this slice (noted in the 2026-06-22 optimize-log).
