# 2026-04-11 — E2E Flaky Test Fix

## What happened
25 E2E tests failed in full suite (766/887 pass). Deep plan created with per-failure root cause analysis assuming real bugs (auth mismatch, API bugs, cascade failures).

## Triage surprise
Phase 0 triage: ran all 25 failures **individually** — every single one passed. Root cause was **parallel execution pressure**, not code bugs:
- Unlimited workers → token contention, API rate limiting, DB locks
- Tests are correct; infrastructure couldn't handle full parallel load

## Fix
2 lines in `playwright.config.ts`:
```
retries: 0 → 1 (local)
workers: undefined → 3
```

Result: 802 pass / 0 fail / 45 skip (was 766/25/46).

## Lesson
- **Always triage before fixing.** The 4h plan with per-test fixes would have been wasted effort.
- Phase 0 (capture actual errors) saved 3.5h by revealing the real root cause in 15min.
- "25 failures" sounds like 25 bugs. Reality: 1 infrastructure issue manifesting 25 times.
- Deep red-team analysis assumed worst case (real API bugs). Triage proved otherwise.

## Remaining
45 skipped (env-conditional) + 19 DNR (browser/compliance projects) tracked in T3-03.
