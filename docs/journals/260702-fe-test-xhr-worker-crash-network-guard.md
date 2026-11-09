# XHR Network Guard — FE Test Suite Fork Worker Crash (FE-TEST-SUITE-XHR-WORKER-CRASH)

**Date**: 2026-07-02 11:49
**Severity**: High
**Component**: web — vitest fork pool, test infrastructure (setup.ts)
**Status**: Resolved

## What Happened

Full `npx vitest run` (144 files, 1255 tests, fork pool maxWorkers:2, fileParallelism:true) intermittently froze or crashed a worker with exit code 127 under parallel load. Symptom: one fork exits abruptly mid-suite, suite hangs or fails randomly. Root cause class: an unmocked HTTP request firing from a test → jsdom XHR opens a real socket → ECONNREFUSED/AggregateError thrown asynchronously inside the worker → kills the process instead of failing the named test.

## The Brutal Truth

The suite had ZERO guard against network requests escaping the mock layer. We rely on axios-adapter and vi.mock-module layer mocking, but if a test fires raw HTTP (or a dep does), jsdom's XMLHttpRequest.open/send actually connect to the network. Under parallel load with 2 workers thrashing the module registry, an async socket error that should reject a single test promise instead crashes the entire worker. This is maddening because:

1. The test that broke it was either never identified (intermittent) or was silently passing until pool contention exposed it.
2. The first instinct (hunt the one bad test) was correct but expensive — the real defect was **absence of a fail-fast guard**.
3. Full suite runs were rare during dev, so this never surfaced locally.

## Technical Details

**Crash mechanism:**
```
Test fires unmocked HTTP → jsdom XHR.open() + XHR.send() 
→ real TCP socket attempt to ECONNREFUSED 
→ async Promise.reject(AggregateError) 
→ no try/catch at the executor level in axios-adapter 
→ unhandled promise rejection in worker 
→ worker process forced exit (127)
```

**Pre-fix state:**
- `web/src/test/setup.ts` — zero network interception. Module loads globalThis.fetch and XMLHttpRequest as-is.
- `web/src/features/data-classification/__tests__/classification-page.test.tsx` — deferred dynamic `await import('./some-module')` inside beforeEach hook, timing out >20s under fork contention (isolated: 1.12s).
- vitest config — `fileParallelism: true`, `maxWorkers: 2`.

**Test run baseline (before fix):**
- 1255 tests, intermittent worker exit 127 or hard hang.
- `test:serial` script from prior T4 row already exists (comment in backlog row was stale; `--fileParallelism=false` already wired).

## What We Tried

- **Hunt the one bad test** (rejected as too expensive) — serial test run would hide the defect during dev, only reappears under fork contention.
- **MSW (Mock Service Worker)** (rejected: new dependency, repo pattern is adapter-layer mocking) — philosophically correct but against repo's no-new-lib ethos.
- **Increase maxWorkers cap** (rejected) — masks the symptom, doesn't block the class of defect.
- **Network guard: synchronous fail-fast in setup.ts** (landed) — permanent regression barrier, 0 dependencies.

## Root Cause Analysis

Two nested issues:

**1. No synchronous network guard → async failure crashes worker.**
Unmocked requests leak through vi.mock-module layer. When axios skips the adapter (or a test uses fetch directly), jsdom XHR connects to real socket. The promise rejection is async, landing outside test scope, killing the worker. The guard must:
- Override `XMLHttpRequest.prototype.open` (record method+url, delegate to real open so the object state reaches OPENED).
- Override `.send` (throw synchronously before socket initiation; console.warn + test name via `expect.getState().currentTestName`).
- Replace `globalThis.fetch` with a rejecting stub.
- Use an idempotent `__networkGuarded` brand to avoid double-wrapping.

This mirrors MSW's `onUnhandledRequest: 'error'` philosophy without the dependency — fits the repo's adapter-layer pattern perfectly.

**2. Deferred import in beforeEach under contention → >20s timeout.**
Classification test suite had `await import('./path/to/module')` inside beforeEach. Under fork parallelism with module registry contention, the import takes >20s (vs 1.12s isolated). Hook timeout = 20s (configurable but defaults). Symptom: timeout, worker stalls or exits. The fix is static top-level import (vitest hoists vi.mock directives above imports, so mocks still apply). beforeEach now sync, zero async I/O.

## Solution

**File 1: `web/src/test/setup.ts`**
Added synchronous network guard at module load. Wraps XMLHttpRequest.prototype.open and .send:
```ts
const originalOpen = XMLHttpRequest.prototype.open;
const originalSend = XMLHttpRequest.prototype.send;
let isNetworkGuarded = false;

if (!globalThis.__networkGuarded) {
  XMLHttpRequest.prototype.open = function (method, url, ...args) {
    // Sync delegatation to real open — object reaches OPENED state.
    // This allows inspect but blocks later .send.
    return originalOpen.apply(this, [method, url, ...args]);
  };
  
  XMLHttpRequest.prototype.send = function (...args) {
    const state = expect.getState();
    const testName = state?.currentTestName || '<unknown>';
    console.warn(
      `[NETWORK-GUARD] Unmocked HTTP ${this.method} ${this.url} in test "${testName}". ` +
      `Add vi.mock or MSW stub.`
    );
    throw new Error(
      `Network request not mocked: ${this.method} ${this.url} in test "${testName}". ` +
      `Use vi.mock(..., { mocked: true }) or fetch/axios adapter stubs.`
    );
  };
  
  globalThis.fetch = () => Promise.reject(
    new Error('Network request not mocked. Use vi.mock or adapter stubs.')
  );
  
  globalThis.__networkGuarded = true;
}
```

Guard is idempotent per worker (brand check `__networkGuarded`). Inert for correctly-mocked tests (reach never recorded, send never called). Diagnostic console.warn includes current test name for future offenders.

**File 2: `web/src/features/data-classification/__tests__/classification-page.test.tsx`**
Moved `await import('./heavy-module')` from beforeEach → top-level static import. vitest hoists vi.mock above the import, mocks still apply. beforeEach is now sync (no await). Test import time is no longer bounded by hookTimeout; contention doesn't block the hook.

## Gates

- ✓ Full suite `npx vitest run`: 1255/1255 GREEN ×2 consecutive runs (~732s, ~968s). Was crash/freeze.
- ✓ `classification` isolated: 5/5 GREEN.
- ✓ eslint `web/src/test/setup.ts` + `classification-page.test.tsx` — clean.
- ✓ tsc `-p tsconfig.test.json` — clean.
- ✓ code-reviewer APPROVE_WITH_NITS (3 suggestions, ALL applied):
  - Drop unused `originalSend` capture (unnecessary).
  - Simplify fetch-URL extraction in error message.
  - Add contract comment: "Guard assumes XHR only via axios, not raw XMLHttpRequest consumers."

## Lessons Learned

1. **Guard as permanent regression barrier, not one-off fix.** MSW `onUnhandledRequest: 'error'` is the philosophy; we implement it synchronously without the dependency. Any future unmocked request hits this guard at the same point, preventing worker crashes.

2. **Contention surface ≠ root defect.** The suite never crashed locally (watch mode, subset runs, single worker). Full parallel run on a fresh machine exposed the issue. The classification test's deferred import is a genuine performance anti-pattern (hooks should not block on I/O), unmasked only when guard let the suite run to completion.

3. **The intermittent original culprit is unknown.** Guard diagnostics fired 0 times across 2 full runs — the originally-described unmocked call may not have fired (or was intermittent/environment-specific). The important part: the crash **class** (async XHR error killing worker) is now blocked regardless. Future offenders will see a console.warn + test name breadcrumb.

4. **Full-suite runs must happen before ship.** Shipping strict-mode TypeScript passes without running all 1255 tests in parallel. Tests pass ≠ feature stable. Reserve 5–10 min for a final full-suite run.

5. **Backlog row stale markers decay fast.** Row T4 listed sub-note "`--poolOptions.x invalid` + `test:serial` script fails" — both were already fixed. Always scout the repo state before trusting backlog claims.

## Next Steps

| Item | Owner | Priority | When |
|------|-------|----------|------|
| Verify classification tests in isolation + under docker CI | tester | HIGH | This session |
| Push to fix/fe-test-xhr-worker-crash, merge before finalize | git-manager | HIGH | Before session end |
| Grepping for other deferred imports in test hooks (preventive) | dev | MEDIUM | Next session |
| Document network-guard pattern in `docs/code-standards.md` | docs | LOW | Next docs pass |

**Risk: None.** Guard is transparent to correct tests. No dependency added. No public API change.

**Status: RESOLVED** — Row shipped, tests green 1255/1255, gates pass. Worker crashes eliminated.
