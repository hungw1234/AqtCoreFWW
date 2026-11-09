# Journal — Backlog housekeeping: mixed-section trim + P4-2B-FU3 stale-close

**Date:** 2026-05-27 ~16:45–18:11 ICT · **Type:** docs-only housekeeping · **Commits:** `a9db5094`, `f38f6d62` (both pushed origin + bitbucket)

## Session goal

Late-day (`/aqt-next` flagged 11-ship fatigue) wrap-leaning session. User opted for small housekeeping instead of EOD wrap → two `/cook` slices on backlog rows, then dual-remote push.

## What worked

- **`a9db5094` — BACKLOG-MIXED-SECTION-TRIM:** collapsed shipped narrative of two MIXED sections, kept open-only.
  - BP-MVP: row claimed "P0 shipped, P1–P8 in scope" → reality ALL P0–P8 shipped. Collapsed → pointer + verbatim snapshot in `completed.md`; kept 28 open-FU bullets (zero dropped, census-verified).
  - T3-API-ENVELOPE-FE-RENDER: row claimed PARTIAL-CLOSED p118 → reality FULLY CLOSED p120. Whole-section move to `completed.md`.
  - Method: Python line-range splice extracting archive slices **verbatim** from the original file (no retype → no paraphrase drift); CRLF preserved via `(?<!\r)\n` normalization on injected literals only. `backlog.md` 552→508; `completed.md` +94.
- **`f38f6d62` — P4-2B-FU3 stale-close:** the 3 runtime read methods FU3 anticipated "when Slice 2C built" were all delivered BY Slice 2C (v2.137p27f) on `IBusinessProcessRuntimeQueries` (`GetRuntimeContextAsync`→`WorkflowDefinitionId`, `GetStateFormTemplateIdsAsync`, `GetTransitionFormTemplateIdsAsync`), wired into all 3 runtime handlers. Verified at `IBusinessProcessRuntimeQueries.cs:16,25,35,44`. Closed, no code written.
- Dual-remote push: origin (GitHub) + bitbucket (synodus) both `a66c4211..f38f6d62`.

## What didn't / lessons

- **2× audit-at-slice-start (260522-A) wins in one session** — both `/cook` invocations targeted rows whose premise had drifted; the 5-min empirical probe prevented (a) an under-scoped trim that would leave stale narrative + drop an orphan, and (b) building duplicate dead methods.
- **🐛 Orphan-row defect found mid-trim:** `WF-CONCURRENCY-FLAKE` had lost its `###` header and sat directly below the T3 section being moved. A naive "move to next `###`" would have dragged this open DEFERRED row into the archive — exactly the "must not drop open sub-items" risk the trim row itself warned about. Header restored in-pass.
- **Sub-pattern (sharpens 260522-A):** a follow-up row gated on "slice X" is frequently *satisfied BY slice X* — the blocker self-resolves. When picking such a row, grep the target identifier in the code slice X produced **before** planning anything.

## Decisions made

- **Corrected-full trim scope** (user-gated): trimmed against ground-truth (P1–P8 all shipped) not the row's literal "keep P1–P8" text.
- **Skipped `code-reviewer`/`tester` subagents** on both slices: docs-only relocations + a stale-close have no code/build/test surface; substituted empirical verification (open-item census 28/28, `git diff --check` clean, interface-source citation). Defensible KISS scaling of the cook workflow.
- **Left the 2 close-notes inline** (struck-through) rather than moving to `completed.md` — recent-context value at EOD; next optimize pass sweeps them. Avoided same-day re-optimization churn (backlog 508 < 600 threshold).

## Test status

N/A — no code, no build, no tests this session. Verification was structural (zero-drop census + whitespace check + interface-source read).

## Next session

- 2 inline close-notes (`BACKLOG-MIXED-SECTION-TRIM`, `P4-2B-FU3`) archivable at next optimize pass.
- No T1/T2 blocker active. Next actionable: `T2-02` (test plan phases 4-6) · `WF-CONCURRENCY-FLAKE` (P3 deferred state-race test, ~30-60min, header now restored) · `R4-EXEC` (P3, stack-gated).
- When picking any `*-FU` row gated on a now-shipped slice, run the slice-X-satisfies-FU probe first.
