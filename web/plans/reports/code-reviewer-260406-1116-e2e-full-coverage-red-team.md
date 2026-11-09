# Red-Team Review: E2E Playwright Full Coverage Plan

**Plan:** `plans/260406-1109-e2e-full-coverage/plan.md`
**Date:** 2026-04-06
**Reviewer:** code-reviewer (adversarial)
**Verdict:** PLAN HAS 3 CRITICAL, 4 HIGH, 4 MEDIUM ISSUES — requires revision before execution

---

## CRITICAL Findings

### C1. `write-ops` Rate Limiter Has NO Dev Override — Will Block Tests at 20 req/min

**Severity:** CRITICAL
**Category:** assumption, risk
**Evidence:**

The plan mentions "Dev rate limit already raised to 1000 req/min" (Phase 3, Risk section). This is **only true for the GlobalLimiter**. The named `write-ops` policy is hardcoded at 20 req/min with NO Development environment override:

```csharp
// RateLimiterConfiguration.cs:78-84
options.AddFixedWindowLimiter("write-ops", opt =>
{
    opt.PermitLimit = 20;      // <-- FIXED, no isDev check
    opt.Window = TimeSpan.FromMinutes(1);
});
```

Compare to the GlobalLimiter which DOES have a dev override:
```csharp
var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
var permitLimit = isAuth ? (isDev ? 1000 : 100) : (isDev ? 200 : 20);
```

The `write-ops` policy is applied to **130+ controller endpoints** across all modules (every POST/PUT/DELETE). The test suite has 33+ write operations. Running them with `fullyParallel: true` will exhaust the 20/min limit within seconds.

Similarly, `public-form-submit` is capped at 5/min and `mfa-verify` at 5/min — the public-form lifecycle tests alone make 2+ submissions per run.

**Impact:** Tests will hit 429 errors, produce false negatives, and waste debugging hours chasing phantom rate limit failures.

**Recommendation:**
1. Add `isDev` check to ALL named policies, not just the global limiter
2. Or add `ASPNETCORE_ENVIRONMENT=Development` to E2E runner env, AND update the write-ops policy
3. Document rate limits in test infrastructure README

---

### C2. `workflow-transitions.spec.ts` Missing `serial` Mode — Tests Will Fail Under `fullyParallel: true`

**Severity:** CRITICAL
**Category:** assumption, risk
**Evidence:**

`workflow-transitions.spec.ts` shares state (`happyCaseId`, `rejectCaseId`) across 8 sequential tests but does NOT use `test.describe.configure({ mode: 'serial' })`:

```typescript
// workflow-transitions.spec.ts — NO serial config
test.describe('Workflow Transitions (authenticated API)', () => {
  let happyCaseId: string;   // Set by test 1, used by tests 2-5
  let rejectCaseId: string;  // Set by test 2, used by test 6
```

The Playwright config has `fullyParallel: true`. This means tests 2-8 will run before test 1 finishes, `happyCaseId` will be undefined, and all subsequent tests will skip or fail.

Compare to `workflow-definitions.spec.ts` which correctly uses `test.describe.serial(...)`.

**Impact:** Entire workflow transition coverage will be broken. This is NOT a "fix locator" problem — it's a structural test architecture bug.

**Recommendation:** The plan's Phase 3 "Pattern C: Locator mismatch (~5 files)" underestimates this category. Audit ALL 42 blocked files for missing `serial` mode when they share state via `let` variables. This is a distinct fix pattern that the plan doesn't account for.

---

### C3. Plan Claims "42 Files Blocked by @axe-core" — Actual Behavior Is More Nuanced

**Severity:** CRITICAL
**Category:** assumption
**Evidence:**

Verified empirically:
- `npx playwright test e2e/ --list --project=chromium` → **0 tests** (blocked)
- `npx playwright test e2e/crud-users.spec.ts --list --project=chromium` → **3 tests** (works)
- `npx playwright test e2e/ --list --project=admin-browser` → **60 tests** (works)

The blocking is NOT that "42 files can't compile." Individual files compile fine. The issue is that Playwright's test collector tries to compile ALL files matching the project glob, and when ONE file (`axe-audit.spec.ts`) fails to import, the ENTIRE collection step aborts with 0 results.

**Impact on plan:** Phase 1 proposes `npm install -D @axe-core/playwright` as the fix. This will work, but:
1. A simpler/cheaper alternative exists: add `accessibility` to `testIgnore` in the `chromium` project and create a separate `accessibility` project
2. Installing @axe-core brings in a 2MB+ dependency. The axe tests themselves will likely FAIL because they reference `AUTH_SERVER = 'http://localhost:5000'` (wrong port) and require `VITE_RUNNING` env var
3. The plan doesn't account for fixing the axe tests after unblocking them

**Recommendation:**
- Option A (minimal): Add `|accessibility` to chromium's `testIgnore` regex — zero install needed, unblocks all 39 non-accessibility files instantly
- Option B (full): Install @axe-core AND fix ports in all 3 accessibility files AND set up VITE_RUNNING env
- Plan should specify which option and estimate both

---

## HIGH Findings

### H1. 9 Files Still Hardcode `localhost:5000` — Phase 1 Sed Script Won't Catch All

**Severity:** HIGH
**Category:** scope, assumption
**Evidence:**

Plan Phase 1, Step 3 proposes a sed regex: `s|localhost:5000|127.0.0.1:5002|g`. But 9 files contain `localhost:5000` references that serve DIFFERENT purposes:

| File | Usage | Correct Fix |
|------|-------|-------------|
| `docker-integration.spec.ts` | `AUTH_URL` const | Should stay as-is (Docker tests use Docker ports) |
| `smoke.spec.ts` (7 refs) | Direct URLs + string matching | Mixed: some should change, some are for Docker mode |
| `browser-ui.spec.ts` (2 refs) | `waitForURL` regex + string match | Only the regex should use 5002 |
| `axe-audit.spec.ts` | `AUTH_SERVER` const | Should change to 5002 |
| `keyboard-navigation.spec.ts` | `AUTH_SERVER` const | Should change to 5002 |
| `responsive-layout.spec.ts` | `AUTH_SERVER` const | Should change to 5002 |
| `pdpl-compliance.spec.ts` | `AUTH_SERVER` const | Should change to 5002 |
| `signalr-websocket.spec.ts` | `AUTH_SERVER` const | Should change to 5002 |
| `error-scenarios-session.spec.ts` | String match check | Context-dependent |

The blind sed will break `docker-integration.spec.ts` which is intentionally using port 5000 for Docker-mode testing. It will also break `smoke.spec.ts` string assertions that check for `localhost:5000` in URL redirects.

**Recommendation:** Port fixes must be file-by-file review, not bulk sed. Estimate should increase from "~30 files bulk fix" to "30 env-var-driven fixes + 9 manual reviews." Use `process.env.AUTH_SERVER` pattern from `auth-helper.ts` instead of hardcoded ports.

---

### H2. Effort Estimates Are 2-3x Too Optimistic

**Severity:** HIGH
**Category:** effort
**Evidence:**

The debugging session (documented in context) took 2 hours to fix just 4 failures in the browser-ui subset (60 tests that were already compilable). Extrapolating:

| Phase | Estimated | Realistic | Basis |
|-------|-----------|-----------|-------|
| 1: Unblock | 1h | 1-2h | Reasonable if install-only. 2h if fixing axe test ports too |
| 2: Baseline | 2h | 3-4h | Running 200+ tests sequentially + categorizing + rate limit debugging |
| 3: Fix broken | 6h | 12-16h | 42 files x avg 20min each. Includes serial mode bugs, DTO mismatches, unknown form fields, rate limits |
| 4: Gap analysis + new | 7h | 10-14h | Writing new tests for modules with zero coverage is discovery work |
| **Total** | **16h** | **26-36h** | **1.6-2.25x the estimate** |

Evidence for Phase 3 being most underestimated:
- Browser-ui debug: 4 failures in 6 known-good files took 2h → ~30min/failure
- 42 new files with unknown failure modes = at minimum 1 failure per file = 42 × 20min = 14h
- Some files (workflow-transitions) have structural bugs requiring redesign, not just locator fixes

**Recommendation:** Either:
1. Adjust estimates to 30h+ and split into 2-3 sprint blocks
2. Ruthlessly cut Phase 4 scope (see M1)
3. Add buffer: "Phase 3 may take 2-3 days, checkpoint after each batch of 10 files"

---

### H3. `fullyParallel: true` + Shared Test Data = Cross-Test Pollution

**Severity:** HIGH
**Category:** risk, assumption
**Evidence:**

`playwright.config.ts` sets `fullyParallel: true`. Multiple test files operate on the same entities:
- `crud-users.spec.ts` lists/modifies users
- `admin-sessions.spec.ts` queries sessions for the same admin user
- `admin-access-reviews.spec.ts` queries access reviews
- `error-scenarios-api.spec.ts` creates system-params and cases
- `workflow-transitions.spec.ts` creates cases shared across tests (and lacks serial mode — C2)

When running in parallel:
1. Test A creates a user, Test B lists users — B may or may not see A's user
2. Test A deletes a case, Test C tries to transition that case → 404
3. `findRowByText` returns wrong row index because another parallel test inserted a row

Only 17 of 53 files use `serial` mode. The remaining 36 files will run with arbitrary interleaving.

**Recommendation:**
1. Phase 2 baseline MUST run with `--workers=1` first to establish clean baseline
2. Plan should explicitly state: "parallel is a stretch goal for Phase 4, not Phase 3"
3. Test data factory `Date.now()` suffix prevents name collisions but NOT state collisions

---

### H4. Tests Accept 500 as Valid — Will Silently Pass on Server Errors

**Severity:** HIGH
**Category:** assumption
**Evidence:**

Two test files accept HTTP 500 as a passing status:
```typescript
// full-route-rendering.spec.ts:71
expect([200, 204, 403, 500]).toContain(res.status());

// gap-closure-smoke.spec.ts:23
expect([200, 403, 500]).toContain(res.status());
```

Comment says: "500 = DB not migrated in dev." This means if the API server crashes or throws an unhandled exception, the test will still pass.

The plan does not address tightening these assertions in Phase 3 or 4.

**Impact:** False-positive coverage. Tests "pass" but the system is broken.

**Recommendation:** Phase 3 must include a task to audit all status code assertions. Replace `500` acceptance with:
- `expect([200, 204, 403]).toContain(res.status())` for normal tests
- Add explicit 500-handling tests in `error-scenarios-*.spec.ts` if needed

---

## MEDIUM Findings

### M1. Phase 4 Is Scope Creep — Should Be a Separate Plan

**Severity:** MEDIUM
**Category:** scope
**Evidence:**

Phase 4 "Gap Analysis + New Tests" is:
- 7h estimated (44% of total plan effort)
- Entirely new test authoring, not fixing existing tests
- Depends on Phase 3 output (gap report)
- Has its own success criteria (≥95% pass rate, all modules covered)

The plan title is "Full Coverage" but Phases 1-3 are "make existing tests work." Phase 4 is "write new tests." These are different initiatives with different risk profiles.

**Recommendation:** Split into two plans:
- Plan A (P0): Phases 1-3 — "Unblock + stabilize existing 53 files" (9h)
- Plan B (P1): Phase 4 — "Close coverage gaps" (10-14h, separate sprint)

This reduces blast radius if Plan A takes longer than expected.

---

### M2. No CI/CD Integration Plan

**Severity:** MEDIUM
**Category:** missing
**Evidence:**

The plan has zero mention of:
- How tests run in CI (GitHub Actions? Jenkins?)
- Docker service startup for CI (`docker-integration.spec.ts` requires `DOCKER_RUNNING=true`)
- Whether CI workers have the same rate limit config
- How test results are reported/stored
- Whether the webServer config in `playwright.config.ts` should be uncommented for CI

The plan says `workers: process.env.CI ? 1 : undefined` — CI already uses single worker. But CI also needs:
- Backend + AuthServer running
- Database seeded with test data
- Rate limits configured for test volume

**Recommendation:** Add a Phase 5 or extend Phase 3 with CI setup tasks:
- GitHub Actions workflow for E2E
- Docker Compose for CI backend
- Rate limit env vars for CI

---

### M3. `findRowByText` Known Unreliable — Plan Doesn't Address Root Cause

**Severity:** MEDIUM
**Category:** risk
**Evidence:**

Context from debugging session: "`findRowByText` sometimes fails even when row exists (textContent matching issues)." The current implementation:

```typescript
// table-helper.ts:61-68
export async function findRowByText(page: Page, text: string): Promise<number> {
  const rows = page.locator('.ant-table-tbody tr.ant-table-row');
  const count = await rows.count();
  for (let i = 0; i < count; i++) {
    const rowText = await rows.nth(i).textContent();
    if (rowText?.includes(text)) return i;
  }
  return -1;
}
```

Problems:
1. `textContent()` concatenates ALL cell text without delimiters → `"John" + "Doe"` = `"JohnDoe"`, searching for "John D" fails
2. No wait/retry logic — race condition between DOM update and check
3. Returns -1 silently — caller must check, many callers may not

Plan Phase 3 "Pattern C: Locator mismatch" mentions this but only as a locator fix, not as a fundamental helper reliability issue.

**Recommendation:** Fix `findRowByText` to use `innerText()` (includes spacing) or per-cell search. Add retry/waitFor logic. This is infrastructure debt that will cause flaky tests across ALL files.

---

### M4. Token Expiry Not Handled in Long Test Runs

**Severity:** MEDIUM
**Category:** risk
**Evidence:**

`auth-fixture.ts` acquires tokens once per worker via `scope: 'worker'`. If the full suite takes 30+ minutes (likely with 200+ tests running sequentially), ROPC tokens may expire mid-run.

The `getAccessToken` function makes a single call and caches the result:
```typescript
apiToken: [async ({ playwright }, use) => {
  const ctx = await playwright.request.newContext();
  const token = await getAccessToken(ctx);
  await use(token);  // <-- used for entire worker lifetime
  await ctx.dispose();
}, { scope: 'worker' }],
```

If token lifetime < total test duration, later tests will get 401s.

**Recommendation:** Add token refresh logic or shorter worker scope. Check token expiry in `authHeaders()`.

---

## LOW Findings

### L1. `docker-integration.spec.ts` Will Always Be Skipped

The file has `test.skip(() => !process.env.DOCKER_RUNNING, ...)`. The plan doesn't mention setting this env var. Not a bug, but 7 tests will always show as "skipped" in the baseline, inflating the skip count.

### L2. Performance Baselines Hardcode Thresholds

`performance-baselines.spec.ts` has hardcoded timing assertions (e.g., "under 1s"). In CI with cold containers, these will likely fail. Not addressed in the plan.

### L3. `error-scenarios-concurrent.spec.ts` Concurrency Test Is Not Actually Concurrent

The test sends two sequential requests, not parallel. It tests "stale token" not "concurrent write." The test name is misleading and the plan's Phase 4 gap analysis doesn't flag this.

---

## Summary Matrix

| ID | Severity | Category | Finding | Effort to Fix |
|----|----------|----------|---------|---------------|
| C1 | CRITICAL | assumption | `write-ops` rate limiter fixed at 20/min, no dev override | 30min BE change |
| C2 | CRITICAL | risk | `workflow-transitions.spec.ts` missing serial mode | 10min per file (audit all 42) |
| C3 | CRITICAL | assumption | Axe-core blocking is collector-level, not compile-level; simpler fix exists | 15min |
| H1 | HIGH | scope | 9 files with `localhost:5000` need manual review, not bulk sed | 2h |
| H2 | HIGH | effort | Total effort 2-3x underestimated (26-36h vs 16h) | Plan revision |
| H3 | HIGH | risk | `fullyParallel` + shared test data = cross-test pollution | Baseline with workers=1 |
| H4 | HIGH | assumption | Tests accept 500 as valid status | 1h audit |
| M1 | MEDIUM | scope | Phase 4 should be separate plan | Plan split |
| M2 | MEDIUM | missing | No CI/CD integration plan | Add phase |
| M3 | MEDIUM | risk | `findRowByText` structurally unreliable | 1h rewrite |
| M4 | MEDIUM | risk | Token expiry in long runs | 30min fixture update |

---

## Recommended Plan Revision

1. **Before Phase 1:** Fix `write-ops` rate limiter to include `isDev` check (BE change, 30min)
2. **Phase 1 (revised):** Add `accessibility` to chromium testIgnore OR install @axe-core + fix ports in 3 files. Estimate: 1-2h
3. **Phase 2 (revised):** Run baseline with `--workers=1`. Audit for missing `serial` mode. Estimate: 3-4h
4. **Phase 3 (revised):** Fix patterns A-D + NEW Pattern E (missing serial mode). Port fixes must be manual, not sed. Estimate: 12-16h
5. **Phase 4:** DEFER to separate plan. Only proceed if Phases 1-3 complete under 20h
6. **Phase 5 (new):** CI/CD integration

**Revised total:** 16-22h for Phases 1-3 only. Phase 4 adds 10-14h in a follow-up plan.

---

## Unresolved Questions

1. What is the ROPC token lifetime? If <30min, token refresh is a P0 blocker
2. Is there a Jenkins pipeline for E2E already, or is this greenfield CI?
3. Should `docker-integration.spec.ts` be excluded from the main suite permanently?
4. Are the `write-ops` rate limits applied at the ASP.NET pipeline level (before controller) or as action filters? This affects whether API tests using `request.post()` are throttled differently than browser tests
5. The plan says "17 BE modules" but the module-test map shows 17 rows. Are modules like `alerting`, `backup`, `dead-letters` (host controller-level) counted? They have no E2E tests and no plan to add them

---

**Status:** DONE
**Summary:** Plan has 3 critical, 4 high issues. Primary risks: write-ops rate limiter will block tests (not dev-overridden), workflow-transitions has a parallelism bug, effort is 2-3x underestimated. Phase 4 should be deferred to a separate plan.
**Concerns:** The `write-ops` 20/min limit is the single biggest risk — it will cause cascading 429s across the entire test suite and is invisible until you actually run the tests.
