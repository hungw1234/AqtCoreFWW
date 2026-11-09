# T4 Cleanup Sweep — Dispositions & Scoping Discipline

**Date**: 2026-06-20 13:30
**Severity**: Medium
**Component**: Backlog curation, identity search, signing, test harness
**Status**: Resolved

## What Happened

Executed `/cook "dọn dẹp hoàn toàn T4"` — a scope-bounded cleanup of the T4 (Polish/Low) backlog tier. Not a full-implementation sprint; instead, a disciplined audit to fix the cheap-safe anomalies and **re-affirm (not close) the genuinely gated rows** with clear dispositions so the backlog reads as intentional, not stale-by-neglect.

5 commits shipped: 3 fixes + 1 test hardening + 1 docs reconciliation.

## The Brutal Truth

T4 is the escape hatch for "it's correct but needs cleanup or long-lead dependencies." The frustrating part is that backlog rows systematically understate scope and gloss over real decision gates. Without empirical probe (INVARIANT 260522-A), you end up re-opening false-stale rows or worse, closing something gated on vendor schema waiting for next month. Real cleanup requires **reading the ground truth**, not trusting the row text.

The flip side: when you do that work, you find actual bugs (null guards, dead SQL) hiding in the T4 bucket because they're not user-facing crashes—just code that's silently inert.

## Technical Details

### USERS-EMAIL-SEARCH (c3b1d6fd)

**Finding**: ListUsersQueryHandler and SearchUsersQueryHandler both contained an OR-ed `Email LIKE '%{input}%'` predicate.

**Ground truth**: Email is AES-GCM-encrypted at rest. Plaintext LIKE compares the user's typed plaintext to ciphertext—mathematically always false. Zero results ever matched this branch.

**Fix**: Dropped the dead term. FullName search retained; equality lookups via NormalizedEmailHash (salted hash of lowercased email) still work.

**Audit lesson**: Row understated scope (claimed "1 handler" → found 2). SearchUsersQueryHandler was added later and the pattern copied without thinking. Always empirical-grep the identifier before closing.

### WFE-RATELIMIT-TEST-DISABLE (f657407c)

**Symptom**: WorkflowParallelBranchRuntimeTests flaked ~30% in parallel runs with HTTP 429 (too many requests). Sequential runs passed.

**Root cause**: AddGovRateLimiter reads `RateLimiting:Enabled` from `builder.Configuration` at service-registration time. WebApplicationFactory's `ConfigureAppConfiguration` in-memory override runs **after** DI configuration. The override never reached the limiter because the value was already read and cached at registration. Real limiter (20 writes/min) enforced, dense test traffic hit it, 429.

**Fix**: Set `RateLimiting__Enabled=false` as an environment variable in DatabaseFixture (same location as ConnectionStrings/Redis/Vault overrides). Env vars reach ConfigurationManager at builder-construction time, before service registration.

**Verification**: 5 consecutive test runs × 3 test sessions, no 429. Sequential + parallel both pass.

**Lesson**: The codebase already learned this constraint for other config keys (Vault timeout, Redis endpoint). The ratelimiter author did not read the existing DatabaseFixture pattern. Single-source-of-truth for test harness config would help—file a follow-up to consolidate all env-var overrides into one documented location.

### SIGNING-NULLCHECK (658b4f47)

**Latent crash**: SigningStepQueryService.GetStateForRequest calls `Dictionary.TryGetValue(UnderlyingRecordId, ...)`. UnderlyingRecordId is `string?`. If null, Dictionary throws ArgumentNullException (null key disallowed).

**Trigger**: Remote PAdES PendingTransaction row with null UnderlyingRecordId.

**Fix**: Guard null before lookup; short-circuit to "not-resolvable" state (same behavior as record-not-found).

**Why in T4**: Not user-facing crash in current code paths (Phase 5 PAdES execution surface is feature-gated). Review confirmed real latent crash waiting for that gate to open.

### CCCD-PUT-ABSENT-MEANS-UNCHANGED (ff0a98eb)

**Row claim**: User can blank CCCD by sending empty string in PUT, should stay no-op.

**Re-affirmation**: WONTFIX-by-design. The full-replace PUT payload carries user detail including stored CCCD. FE correctly gates the save button on "detail still loading or errored" (if you don't wait for detail to load, you'd wipe the CCCD by accident). The save gate **is the right layer**. A sentinel value at the API layer would break "clear CCCD by submitting empty field" (a valid intent). Test added to prevent regression: 3/3 assertions (save blocked while loading, blocked + error-alert on 500, enabled once detail loads).

**Lesson**: User made a deliberate policy choice; backlog auditor confused it for a bug. Document it so next reader doesn't re-open it as a "missing feature."

## What We Tried

1. **Scope negotiation (pre-fix)**: Used AskUserQuestion to confirm user wanted "fix the safe ones + investigate WFE-RATELIMIT + test CCCD gate" — not a full epic sprint. Got explicit approval; shaped downstream work.

2. **Empirical probe on each row**: Grepped the identifier, ran tests, looked at actual SQL/code instead of trusting row text.

3. **Reviewer check on delegated WFE fix**: Debugger agent also bumped NCalcSync 5.12.0 → 6.2.0 (MAJOR version, NU1902 warning unrelated to the rate-limiter fix). Reverted it immediately — out-of-scope, no behavior proof, and MAJOR dependency bumps carry risk. Filed BE-NUGET-NCALCSYNC-NU1902 for a focused follow-up.

## Root Cause Analysis

**Why these bugs existed in T4**:
- USERS-EMAIL-SEARCH: Pattern copied without reading the data contract (Email encrypted). Copy-paste inheritance kills code review.
- WFE-RATELIMIT: Harness config-timing assumed ConfigureAppConfiguration ran before DI; missed the existing DatabaseFixture pattern used for other keys.
- SIGNING-NULLCHECK: Defensive programming oversight; null-coalesce operator existed but wasn't reached in test paths (Phase 5 gate not yet open).
- CCCD-PUT: Not a bug. Row author misread the FE logic.

**Why backlog audit is expensive but non-negotiable**: Rows decay in accuracy faster than the ship cadence. A 7-day-old "Pending" row has <50% chance its claimed status matches the repo. The only cure is empirical probe per INVARIANT 260522-A.

## Lessons Learned

1. **Backlog row scope is always understated.** Grep the identifier; don't trust the prose. 2 handlers, not 1. The row author didn't search.

2. **Config-timing in test harness**: If your test reads a config value at registration time, override it **at the environment-variable or builder-construction level**, not at the WebApplicationFactory.ConfigureAppConfiguration level. The latter is too late. Document this centrally so debuggers don't re-learn it.

3. **Review delegated fixes completely**. Subagents scope-creep; a major dependency bump smuggling in on an unrelated fix is a gotcha. `git diff` the final commit before merge.

4. **Deferred rows need dispositions, not silence.** 7 rows genuinely gated by vendor schema / prod-data probe / running stack / user decision / epic scope. Document the gate so the next reader knows it's intentional, not abandoned.

5. **Dead code is invisible in tests.** The Email LIKE branch covered by mocked equality tests (NormalizedEmailHash path). Add scenario tests for the exclusion cases (plaintext column search should fail on encrypted columns; add to code-standards doc).

## Next Steps

- **Priority 0**: Merge these 5 commits to main (code review APPROVE_WITH_NITS, 0 Critical/High/Medium findings).
- **BE-NUGET-NCALCSYNC-NU1902**: File focused follow-up for NCalc MAJOR version bump (debug-build only, NU1902 Release-build warning). Out of T4 scope.
- **BE-TESTHARNESS-CONFIG-CONSOLIDATION**: Document all env-var overrides in DatabaseFixture as one consolidated source, with comments linking to ConfigurationManager registration timing rules.
- **Carry 7 deferred T4 rows**: Dispositions recorded in backlog.md; re-open only if gate changes (vendor schema shipped, prod data available, user decides, epic unblocked).

**Verification**: Identity.Application 13/13 · WFE integration 5/5 ×3 · FE modal regression 3/3 · 0 public-contract change · 0 lint/typecheck failures.

---

**File**: `C:\AqtCoreFW\docs\journals\journal-260620-1325-t4-cleanup-sweep.md`
