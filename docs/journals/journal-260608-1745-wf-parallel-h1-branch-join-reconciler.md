# H1 Branch-Join Reconciler — WF-PARALLEL-RUNTIME-P5-FOLLOWUP (b)

**Date:** 2026-06-08 · **Session:** `/cook H1 reconciler` (interactive, STOP-before-commit appetite)
**Outcome:** SHIPPED — feat + docs + journal, pushed origin+bitbucket (ahead=0). Closes the last open P5-FOLLOWUP item; the parallel-fork runtime hardening is now complete.
**Type:** feat (correctness/runtime backstop) — Workflow.Infrastructure background job. Ships DORMANT.

## What shipped
NEW recurring Hangfire job `BranchJoinReconcilerJob` (every 5 min) that finishes parallel-branch
joins whose condition is satisfied but whose instance never advanced to the continuation state — a
strand left by a crash between the child-resolution commit and the join transition, or by a genuine
transient concurrency conflict that the synchronous `BranchJoinCompletionService` winner-election
(step 2) explicitly surfaces "for an async reconciler to finish."

Thin finder-delegator: raw-SQL candidate scan → one DI scope per candidate → in-memory
`CheckJoinCondition` recheck → delegate to `IBranchJoinCompletionService.CompleteJoinAsync`. The job
NEVER reimplements completion — that service already owns the RowVersion winner-election, the
continuation fail-CLOSED pre-check, and continuation staging, and is already concurrency-safe against
the live synchronous resolve path. Reimplementing it is the single biggest way this job could have
become a correctness hazard; the whole design is built to avoid that.

## The decisions that mattered (all user-gated)
- **Narrow predicate.** Only branches still `InProgress` whose join condition is NOW met and whose
  instance is still parked at the branching state. The backlog row said "covers timeout-strand," but
  that framing was wrong: the timeout job sets status → `TimedOut` (excluded), and a timed-out AND
  branch fails `CheckJoinCondition` anyway. Advancing a *failed* join would need a failure-target
  state the FSM does not model — that's a separate feature, not this job. Surfaced the contradiction
  to the user rather than silently picking; user chose narrow.
- **Cheap throttled-warn for the empty-continuation config strand.** An empty / handler-less
  continuation fails identically every cycle (`NoHandlerResolved`/GOV_WFL_014). A 5-min job that
  retries a permanently-failing mutation forever is itself a prod hazard (log spam / alert fatigue).
  Detected and logged ≤1×/strand/1h via `IMemoryCache` — no persistence, no migration. Distinguished
  from transient conflicts (which DO retry next cycle). User chose cheap over a persisted attempt-counter.
- **One DI scope per candidate** (`IServiceScopeFactory`) — deliberate deviation from the sibling
  jobs' single-context loop, justified by `CompleteJoinAsync`'s transaction + staging complexity:
  isolates a failure (and its change-tracker rollback) from the next instance.

## The #1 risk, and how it was discharged (not assumed)
`CompleteJoinAsync` runs through the Application `IWorkflowInstanceRepository`, which is subject to
the `ModuleDbContext` tenant global query-filter — but the job runs in a background scope with NO
request tenant context. If that path were blocked, the reconciler would silently no-op in prod.
Test 1 (`StrandedJoin…CompletesJoin`) is the empirical gate — it runs the job through a no-HttpContext
scope and asserts the instance advances. Green. Code-review then independently traced the primary
sources: no HttpContext → `BackgroundJobTenantContext` (SystemAdmin, `TenantId=null`) → the tenant
filter clause becomes pass-through. The test does NOT override `ITenantContext`, so the green
genuinely exercises the prod path, not a seeded-tenant shortcut.

## The bug found in testing (empirical, not guessed)
Test 2 failed its throttle assertion. Rather than guess, I pulled the job's own log lines from a
re-run: `InvalidOperationException: Cache entry must specify a value for Size when SizeLimit is set`,
thrown from the `cache.Set`. The shared `IMemoryCache` is registered with a `SizeLimit`
(`Cache:L1:MaxEntries`), so every entry MUST carry a `Size`. The per-candidate `catch` swallowed the
exception as "Skipped," so the permanent path silently never armed the throttle. Fix:
`MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = window }` — matches the
`MfaTempTokenService` convention. Note: the `isPermanent` detection was correct all along; only the
cache write was misconfigured. The lesson: a swallow-and-continue catch is correct for per-item
isolation but will hide a real misconfiguration — the log line, not inspection, found it.

## Verification
Build 0W/0E (Infrastructure + Api host + tests). Integration `WorkflowBranchJoinReconcilerTests` 4/4
(stranded-recovered / permanent-not-advanced+throttled / incomplete-untouched / healthy-no-op — the
last guards against double-apply on a healthy instance). Full Workflow integration namespace
**103/103 green** (no regression; this run had no rate-limit flake). Code-review APPROVE_WITH_CONCERNS,
0 Crit / 0 High (1 Low — a plan-artifact ref in a test comment — applied; 3 Info accepted).

## Reusable lessons
- **When a backlog row's framing contradicts the engine, trust the engine and re-decide scope with
  the user.** "Covers timeout-strand" was a plausible-sounding requirement that the actual status
  machine made wrong. A 5-min scout of the timeout job + `CheckJoinCondition` refuted it before any code.
- **A background job that mutates through the Application layer inherits its tenant/RLS assumptions.**
  Don't assume the DbContext-direct precedent (SlaBreachCheckerJob) transfers — verify the repo path
  from a no-tenant scope, and make a test the gate for it.
- **Swallow-and-continue hides misconfiguration.** The per-candidate catch is right for isolation, but
  it masked the cache-Size bug; the job's own structured log was the fastest path to the real cause.

## Carry-forward
- Optional: OR / N-of-M join types aren't exercised by the 4 facts (the reconciler is type-agnostic —
  it delegates evaluation to the already-tested `CheckJoinCondition`), so this is a coverage note, not
  a gap.
- Optional fault-injection follow-up: BP fork rollback atomicity (the RT13 companion gap).
- `WFE-RATELIMIT-TEST-DISABLE` — the integration rate-limit flake is still un-root-caused (this run
  happened to be clean).
- Per-host `IMemoryCache` throttle is process-local; only relevant if the deployment scales out.
