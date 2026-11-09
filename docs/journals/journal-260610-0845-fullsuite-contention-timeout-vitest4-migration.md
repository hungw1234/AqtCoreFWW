# Journal — FE Suite Contention Timeout + Vitest 4 Pool Migration (2026-06-10 ~08:45)

**Slice:** `/cook WFE-FULLSUITE-CONTENTION-TIMEOUT --auto` · commit `d354440f` (fix) · test-harness only.

## What shipped

`web/vitest.config.ts`: `testTimeout`/`hookTimeout` 5000→20000 + Vitest 4 pool-rework migration (`poolOptions.forks.{maxForks,minForks,execArgv}` → top-level `maxWorkers: 2` + `execArgv`; `minForks` dropped, no V4 replacement). Full suite 993/993 ×2 at final config, 0 deprecation warnings.

## The discovery that re-scoped the slice

The row treated the Vitest 4 `poolOptions` deprecation as a cosmetic aside ("worth a migration pass"). It was load-bearing: **Vitest 4 removed `poolOptions` and silently ignored the whole block**, so the deliberate `maxForks: 2` RAM cap and 1GB heap cap were never in effect — the suite ran ~7 uncapped forks on an 8-CPU box. The row's "2-fork contention" root cause was wrong about the *effective* state; contention came from 7 heavy antd+jsdom forks fighting for CPU.

Empirical sequence that exposed it:
1. Applied timeout bump alone → clean run 1: **991/993** (2 fail) — contradicting yesterday's "993/993 ×2" verification.
2. Run 2 (self-polluted by an overlapping isolation run): 2 different tests failed, one at 20.9s — past even the new cap.
3. Run 3 clean: 993/993. So bump-alone = improved but still flaky (~1/2 clean runs red).
4. Deprecation line in the captured log → migration guide → restored caps → runs 4+5 both 993/993, tests-phase 161-194s vs 330-381s.

## Lessons (reusable)

- **A deprecation warning on dead config is a state bug, not lint noise.** When a config block is "deprecated/removed", the question is not "should we tidy it" but "what behavior have we been running WITHOUT". Here the answer invalidated the row's diagnosis.
- **Flake verification needs N≥2 clean runs *and* attention to which fix actually moved the needle.** Yesterday's 2× green at bump-alone did not replicate today (1/2 clean runs red). Only after restoring worker caps did green become stable. Don't let one green run close a flake row.
- **Don't pollute your own evidence:** running an isolation probe while the full suite ran in background manufactured artificial contention and one misleading data point (discarded from the verdict).
- **INVARIANT 260522-A held again (6th reinforcement):** row premise partially wrong (cap "deliberate" but not in effect) — found only by empirical probe, not by trusting the row.

## Residual / carry

- 2 pre-existing unhandled rejections (`credential-policies-page.test.tsx`) reported even on green runs; don't fail tests; revisit only if they start failing files.
- Open-Q carried: does the FE suite run in CI?
- Wall-clock now ~478s (2-worker cap in force — the documented RAM trade-off, previously an illusion).

## Verification

993/993 ×2 (runs 4+5, final config) · 0 deprecation · code-review APPROVE 0C/0H/0M (option names grep-verified against installed 4.1.4 types — `any` cast bypasses tsc) · depmap unaffected · no plan dir (inline plan, single-file config change).
