# Vitest Fork Pool Resource Cap (T5-22 Pivot)

**Date**: 2026-04-14 18:29
**Severity**: High
**Component**: web — `vitest.config.ts`, `package.json`
**Status**: Resolved (commit `cb71efb8` on `fix/t5-22-vitest-regressions`)

## What Happened

Immediately after T5-21-C closed, user ran the full vitest suite on an 8-CPU box. 720 tests
passed, 4 failed (pre-existing), 161s wall time. The problem was not correctness — it was
the machine nearly dying to get there. RAM/CPU overload reported during the run.

Root issue: `pool: 'forks'` default forks `availableParallelism() - 1 = 7` workers on an
8-core machine, no V8 heap cap per worker. Vitest test workers are memory-heavy (jsdom +
React 19 + antd + vi.mock registry). Peak RSS 2–4 GB unbounded, concurrency 7 default, the
box runs out of headroom fast.

This was never caught because local dev usually runs subset (`--reporter=verbose`, watch
mode, file filters). Running 720 tests flat with default config is apparently a first.

## The Brutal Truth

We shipped a strict-mode CI fix without ever stress-testing the full suite locally. That's
embarrassing. The config file had zero resource controls — not even a comment acknowledging
that forks have no heap cap by default. We handed a 161s, 720-test suite with 7 uncapped
workers to a mid-range dev machine and were surprised it melted. Iteration 1 (maxForks: 4)
didn't fix it either. Had to go to maxForks: 2 and a hard V8 heap cap before it behaved.
The frustrating part: both of those knobs are in the Vitest docs. They just weren't wired.

## Technical Details

**Baseline (before this session):**
```ts
pool: 'forks',
// no maxForks, no execArgv — defaults to availableParallelism()-1 = 7
```
Peak worker RSS: 2–4 GB unbounded, total ~14–28 GB peak under full concurrency.

**Iteration 1 (rejected — still overflowing):**
```ts
poolOptions: { forks: { maxForks: 4, minForks: 1 } },
maxConcurrency: 5
```

**Iteration 2 (landed):**
```ts
poolOptions: {
  forks: {
    maxForks: 2,
    minForks: 1,
    execArgv: ['--max-old-space-size=1024'],  // hard V8 heap cap per worker
  }
},
maxConcurrency: 3,
sequence: { shuffle: false }
```

**New npm script added (`package.json`):**
```json
"test:serial": "vitest run --poolOptions.forks.singleFork=true --fileParallelism=false"
```
Panic switch for GitHub free-tier runners (2c/4GB). Slower but stays alive.

**Result:** 720/4 pass/fail unchanged. Wall time 161s → 187s (+16%). Worker pool RSS bounded
~2 GB hard ceiling.

## What We Tried

- **maxForks: 4** — not enough. 4 workers × 2–4 GB each still blows a 16 GB box under
  peak concurrency.
- **Reducing maxConcurrency without execArgv** — throttles scheduling but does not bound
  per-worker heap. The worker with the biggest test file still grows unbounded.
- **`isolate: false`** — evaluated and rejected (see Root Cause section).
- **`vmThreads` pool** — evaluated and rejected. Untested with jsdom + React 19 + antd.
  That combination has known quirks with VM contexts. Risk > reward when the forks fix works.

## Root Cause Analysis

Two separate issues compounded:

**1. `NODE_OPTIONS` does not propagate to fork workers.**
Setting `NODE_OPTIONS=--max-old-space-size=1024` in the shell or in `.env` has zero effect
on Vitest fork workers. Forks are spawned with their own process environment; the parent's
`NODE_OPTIONS` is not inherited unless explicitly forwarded. The correct path is
`poolOptions.forks.execArgv` — this injects V8 flags directly into each forked process's
argv before the V8 engine initializes. This is the key insight from the session. Anyone who
tries to cap heap via environment variable and wonders why it doesn't work: it doesn't. Use
`execArgv`.

**2. `isolate: false` is off the table due to `import.meta.env` stubs.**
The obvious way to reduce per-worker overhead is `isolate: false` — shared module registry
means no re-import phase per file, dramatically lower memory. Cannot do it here: existing
tests stub `import.meta.env` at module level using `vi.stubEnv` and `vi.mock`. With a
shared module registry, env state from one test file leaks into the next. This is not a
theoretical risk — it's the exact pattern used across auth and RBAC tests where
`import.meta.env.VITE_AUTH_ENABLED` is toggled per suite. `isolate: false` would
silently corrupt those test environments.

## Lessons Learned

- **`NODE_OPTIONS` in the parent process does not cap fork worker heaps.** Only
  `poolOptions.forks.execArgv: ['--max-old-space-size=N']` works. Do not document
  "set NODE_OPTIONS" as a workaround — it will be tried, it will fail, and someone will
  spend an hour debugging it.

- **Default vitest `pool: 'forks'` config is unsafe for large suites on constrained hosts.**
  `availableParallelism()-1` is greedy. Any suite running 700+ tests with jsdom + a heavy
  component library needs explicit `maxForks` + `execArgv` heap cap. Treat this as baseline
  config, not optional tuning.

- **`isolate: false` is not a free performance win.** If any test file stubs module-level
  state (env vars, global singletons, module-level mocks), shared isolation corrupts test
  environments. Check before enabling. In this codebase, the answer is: do not enable it.

- **Always run full suite on target hardware before declaring CI ready.** T5-21 series
  hardened TypeScript, wired CI gates, fixed 44 test errors — and nobody ran all 720 tests
  on a real machine until after merge. The resource problem was invisible during incremental
  development.

- **Add a `test:serial` panic script as standard practice.** Free-tier CI runners (2c/4GB)
  will blow up on the default parallelism config. A documented serial-mode script prevents
  the "why is CI hanging" confusion call at 2am.

## Next Steps

| Item | Owner | Priority | When |
|------|-------|----------|------|
| Push `fix/t5-22-vitest-regressions` and merge `cb71efb8` | dev | HIGH | Before T5-22 impl resumes |
| Resume T5-22 vitest regression fixes (advisor-approved plan) | dev | HIGH | Next session |
| Verify `test:serial` works on GitHub Actions free runner | dev | MEDIUM | CI smoke after merge |
| Document `execArgv` heap-cap pattern in `docs/code-standards.md` | dev | LOW | Next docs pass |
