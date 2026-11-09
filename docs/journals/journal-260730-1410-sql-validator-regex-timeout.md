# Journal — SQL validator regex timeout (CI-DISABLED #2 root-fix)

**Date:** 2026-07-30 · **Branch:** main · **Plan:** `260730-1353-sql-validator-regex-timeout`

## What happened

Continuing the CI-DISABLED re-enable arc. After auditing #1 (self-resolved) and #4 (wider filter
leak, deferred to user), tackled #2/#3 (flaky-under-contention). Scout found the real root:
`SqlValidationHelper`'s three regexes use a **100ms** `RegexMatchTimeoutException` window. Under
cross-assembly CPU contention (`dotnet test slnx` runs many assemblies in parallel on a few-core
box), a harmless regex exceeds 100ms → throws → helper returns the pseudo-keyword `"PATTERN_TIMEOUT"`
instead of the real verdict. Test #2 asserts "must start with SELECT", gets "PATTERN_TIMEOUT", reddens.

Row 06-10 flagged this as a "real robustness concern" — the same false-timeout hits prod under load,
silently dropping the real SQL validation. User chose the ROOT fix (raise threshold), not a
test-parallelism cap.

## Delivered

- Raised all three regex timeouts 100ms → a shared `RegexTimeout = TimeSpan.FromSeconds(1)` constant.
  Fail-closed catch blocks (`"PATTERN_TIMEOUT"` / `""`) unchanged; blocklist pattern untouched.
- Rationale (in-code): the pattern is a flat alternation (no nested quantifier) → NOT ReDoS-prone,
  so the timeout is a defense-in-depth belt, not a load-bearing bound. 1s never false-fires under
  contention yet still trips a genuine ReDoS (which hangs sec-min).
- Code-review 0-crit: 5/5 verified, all three patterns confirmed structurally linear, defense intact,
  9 consumers need no edit (uniform return).

## Non-obvious / learned

- **xunit.runner.json `parallelizeTestCollections:false` was already present** on both flaky projects —
  but that caps IN-assembly parallelism only. The contention is CROSS-assembly (`dotnet test slnx`
  runs assemblies in parallel), so the in-project cap never addressed it. The config looked like a
  fix but sat at the wrong tier — same shape as the old vitest `poolOptions`-ignored bug.
- **A test-flake and a prod-robustness bug were the same defect.** The 100ms belt false-fires the
  same way under prod load; fixing the test-visible symptom fixed the real gap. Worth checking, when
  a test flakes on a timeout, whether the timeout is also wrong for production.

## Honest verify limit

Per-project runs (Infra 26/26, App 27/27) prove no-regression + root-removed-by-logic, but do NOT
reproduce the full-slnx cross-assembly contention that caused the flake. Accepted: the fix is a pure
widening of an existing safety margin on a proven-linear pattern. #3 (`CreateReportDefinition`) has a
DIFFERENT diagnosed root (shared-state/parallel-collision) — NOT declared closed by this fix; re-verify
under real contention before closing.

## CI-DISABLED status after today

#1 self-resolved (closed) · #2 root-fixed (this slice) · #3 not-confirmed-closed (re-verify needed) ·
#4 wider filter leak (deferred, user CI-architecture decision). Workflow still `disabled_manually`;
enabling = user decision (Actions off by choice).
