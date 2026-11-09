# Journal — E2E Parallel Orphan Leak: afterAll Cleanup (2026-06-10 ~12:00)

**Slice:** `/cook "xử lý E2E Parallel"` · commit `46c75756` · 5th ship of the day · test-only + dev-DB hygiene.

## What happened

Two orphaned `E2E Parallel` definition pairs (4 live rows) had accumulated in the dev DB from 06-09 spec runs. Root cause was structural, not transient: the spec's cleanup was a **trailing `test()` inside `test.describe.serial`** — serial mode skips every remaining test after a mid-suite failure, *including the cleanup test*, so each failed attempt (retries included) leaked one authored pair. The one fully-green run's pair was properly deleted, which is why the leak pattern was 2-of-3.

## Fix

Cleanup moved to `test.afterAll` — runs even when the suite fails partway, per retry attempt. Review's collect-then-assert suggestion applied: attempt every delete, then `expect(failures).toEqual([])` — a failed first delete no longer strands the second definition, and the Guid-vs-long PK delete regression guard stays loud.

## Lessons (reusable)

- **Cleanup-as-a-test in a serial block is a leak by construction.** Serial mode's skip-on-failure semantics turn the trailing cleanup test into dead code exactly when you need it (the failing runs). Resource teardown belongs in `afterAll`/fixtures, never in a test. The 6→5 test-count drop is correct semantics — cleanup was never a behavior under test; its assertion survives as a hook assertion.
- **Per-attempt accounting:** with `retries` configured, the old shape leaked one pair *per attempt*, not per run — matches the found row count exactly (1 failure with retry = 2 pairs). When auditing leak counts, multiply by retry attempts.
- **expect-inside-loop strands later iterations.** Collect-then-assert gets both properties: complete cleanup AND loud failure.
- **The bug fixed this morning made the dev-DB cleanup safe:** deleting v2-first promoted v1 via the new IsLatest handoff (4×204 on the product path) — a live confirmation of `375d682f` as a side benefit.

## Verification

- Spec 5/5 live ×2 (final shape) · SQL: 0 live `E2E Parallel` rows after both the orphan purge and the latest spec run (afterAll cleaned its own pair).
- code-review DONE 0C/0H — 2 optional Lows applied (collect-then-assert; comment trim "Both stay Draft").
- Test-only; no product code; depmap unaffected.

## Carry

- Push pending (`46c75756`). Stack UP (5001/5002/5173).
- Open-Q carry: does the FE/e2e suite run in CI? (the leak class would matter more there — CI DB hygiene).
