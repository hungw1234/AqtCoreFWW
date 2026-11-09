# T5-21 TypeScript Strict Check for Test Files — Config Landed, CI Silence Broken

**Date**: 2026-04-14 14:38
**Severity**: Critical
**Component**: web — `tsconfig.test.json`, `package.json` (`typecheck:test` script), CI pipeline (`ci.yml`)
**Status**: Partially Resolved (config-only commit `d83c0c51`; fixes split T5-21-B CRITICAL + T5-21-C HIGH)

## What Happened

T5-21 was scoped as a 0.25-day task: add `tsc` strict coverage for `src/test/**` so
fixture DTO drift (caught in T5-17 code review, not at build time) gets caught early.
Simple. Surgical. In and out.

Instead, running the baseline revealed 62 TypeScript errors in test files — and a
separate baseline check on `tsconfig.app.json` turned up 29 errors in production code
that CI should have been blocking for days. Querying `gh run list` confirmed it: all 5
most-recent runs of `ci.yml` on `main` have been `failure` since 2026-04-11. Main has
been silently red for at least three days and nobody noticed.

The meta-win of T5-21 isn't two config files. It's that we finally looked.

## The Brutal Truth

Main CI has been broken for three days. Commits have been landing on a red branch.
Nobody set up an alert. Nobody was watching. `tsc -b` on the root config exits 2 and
we have been merging as though that is normal. A 0.25-day scope expansion found this
because we happened to run a baseline before implementing — which is only remarkable
because we apparently stopped doing it at some point.

The 29 errors in `tsconfig.app.json` are the real embarrassment. Those are production
errors in committed code. They are not test fixtures; they are not type stubs. They are
in the application. The fact that CI was supposed to catch these and did not — because
`tsc --noEmit` without `-b` on a root config with `files: []` is a **no-op** — means
we shipped a type gate that has never worked as a gate. It passes on success it did
not earn.

## Technical Details

- **Baseline 1**: `npx tsc --noEmit -p web/tsconfig.test.json` → 62 errors across test fixture files. Error class: object literal type mismatches, missing required fields introduced by Orval regeneration (T5-19).
- **Baseline 2**: `npx tsc --noEmit -p web/tsconfig.app.json` → 29 errors in production source.
- **Baseline 3**: `npx tsc -b` (from `web/`) → exit code 2. Not flaky. Reproducible.
- **CI verification**: `gh run list --branch main --workflow ci.yml --limit 5` → 5/5 runs `failure`, oldest dated 2026-04-11.
- **Root tsconfig no-op**: Root `tsconfig.json` uses `files: []` and `references: [...]`. Running `tsc --noEmit` (no `-b`) on it type-checks zero files — exits 0, produces zero diagnostics, looks like success. CI was running this form.
- **Config delivered**:
  - `web/tsconfig.test.json`: extends `tsconfig.app.json`, `include: ["src/test"]`, `exclude: []` (array replaces, not merges — required to include test paths excluded by app config)
  - `package.json`: added `"typecheck:test": "tsc --noEmit -p tsconfig.test.json"`
  - 2 code files + 5 plan/report docs committed

## What We Tried

**Rejected: fix-all in auto mode.** 62 errors + 29 prod errors = 91 total. Bulk-fixing
in one PR produces an unreviable diff. Split into T5-21-B (prod fix + CI no-op repair,
CRITICAL) and T5-21-C (test fix + new CI `typecheck:test` step, HIGH). Each will be
independently reviewable.

**Rejected: `continue-on-error: true` on CI step.** Team rules are explicit — do not
ignore failing tests to pass the build. A `continue-on-error` gate is a green checkbox
over a red result. Same failure mode that let main go red undetected.

**Rejected: adding tsconfig.test.json to root `references`.** The root tsconfig drives
`tsc -b` in the build step. Pulling test files into the project reference graph
includes test-only types in the app compilation output. The test config intentionally
lives outside the build graph — it is a quality gate, not a build input.

## Root Cause Analysis

Two separate failures compounded.

**CI no-op gate**: At some point the CI `typecheck` step was wired to `tsc --noEmit`
without the `-b` flag, targeting a root config with `files: []`. That invocation
type-checks nothing. Exit 0 is guaranteed and meaningless. This was never validated
after the tsconfig references restructure — it looked like it was working because it
never failed.

**Governance gap**: No alert on `main` CI status. Failures accumulated silently across
three days and four merges. The process assumption was "CI is red → PR is blocked" but
that only holds if someone (or something) enforces it. Nobody was watching the branch
status dashboard.

The T5-17 fixture drift that prompted T5-21 was a symptom of the no-op gate, not an
isolated incident. The gate has been absent for longer than T5-17.

## Lessons Learned

- `tsc --noEmit` on a references-mode root config with `files: []` exits 0 and checks
  nothing. Always validate your type gate with a deliberate error injection before
  trusting it. If breaking it doesn't turn CI red, CI isn't checking types.
- `exclude: []` in an extending tsconfig **replaces** the parent's exclude list — it
  does not merge. Required here to include paths the app config explicitly excludes.
  Subtle. Worth a comment in the file.
- Red-team the plan before writing code. The advisor review in Phase 01 caught the
  CI no-op before any implementation started. That catch defined the entire shape of
  the work. Skip the review, miss the finding, ship another gap.
- Watching CI on `main` is not optional. A branch that stays red for three days is a
  team process failure, not a pipeline anomaly. Alerts or dashboards; pick one.

## Next Steps

| Item | Owner | Priority | When |
|---|---|---|---|
| T5-21-B: Fix 29 prod TS errors + repair CI no-op gate (`tsc -b` flag) | dev | CRITICAL | Next session |
| T5-21-C: Fix 62 test TS errors + add `typecheck:test` step to CI | dev | HIGH | After T5-21-B merged |
| Alert on `main` CI status (Slack / dashboard) | lead | HIGH | This sprint |

## Open Questions

1. **How long has the CI no-op been in place?** `gh run list` only shows 5 runs. If the
   no-op predates T5-19 (Orval regeneration), the 29 prod errors may be older than the
   current sprint. Worth a `git log --all -- web/tsconfig.json` to establish when the
   references restructure happened.

2. **Are the 29 prod errors blocking any runtime behavior?** TypeScript errors ≠ runtime
   errors, but some of the mismatches may indicate real logic problems (missing required
   fields passed to typed functions). T5-21-B analysis must classify each error before
   bulk-fixing.
