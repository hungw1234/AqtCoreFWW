# Enrollment Nonce Cleanup Job: Bounded Growth via Set-Based Delete

**Date**: 2026-06-25 18:20
**Severity**: Low
**Component**: Signature / Identity Infrastructure
**Status**: Resolved
**Row**: SIGN-ENROLLMENT-NONCE-EXPIRY-SWEEP (T4, non-security)

## What Happened

Added `EnrollmentNonceCleanupJob` (IHostedService + PeriodicTimer 60min) to hard-delete spent `SignerEnrollmentNonce` rows via set-based `ExecuteDeleteAsync`. Predicate: `ConsumedAt != null OR ExpiresAt < now`. Prevents unbounded table growth after nonce consumption flow ships.

## The Brutal Truth

This is a 3-file maintenance slice with no drama—tests green, code-reviewer clean (2 optional nits applied), ship-ready. The only friction was in the design trade-off decision: *no grace period*. Initially second-guessed whether stale nonces needed a buffer (as PendingTransactionCleanupJob does), but reasoning held: a nonce past ExpiresAt is already read-rejected by `IsExpired` check; a consumed nonce is single-use terminal. There is no valid in-flight operation that grace-period cleanup would corrupt. Cut the ambiguity, shipped clean.

## Technical Details

**Files:**
- `src/Modules/Identity/Application/Infrastructure/Hangfire/EnrollmentNonceCleanupJob.cs` (new, 54 LOC)
- `src/Modules/Identity/Infrastructure/Identity/DependencyInjection/IdentityInfrastructureRegistration.cs` (+3 lines)
- `tests/Modules.Identity/Application/Infrastructure/Hangfire/EnrollmentNonceCleanupJobTests.cs` (new, 3 TC)

**Test results:**
- 3 new integration tests: consumed→deleted, expired→deleted, active→retained (all green)
- No vacuous assertions (TC-3 active-retention confirmed row count)
- Regression PendingTransactionCleanupJobTests 4/4 green
- Build 0E/0W

## Design Decisions & Rationale

**1. Set-Based Delete, Not Load-Mutate:**
Mirrored `PendingTransactionCleanupJob` skeleton but diverged on execution: `ExecuteDeleteAsync` (raw set-based SQL) instead of load-then-mutate. Why: bounded growth is the only goal; no side-effect events or audit trails required on nonce deletion. Saves memory + DB roundtrips. Load-mutate pattern only justified when you need entity events or history hooks.

**2. NO Grace Period:**
Diverged deliberately from mirror. PendingTx grace (configurable minutes via options) protects in-flight signatures; premature cleanup would kill a 24h sign-flow. Nonces: `ExpiresAt` is read-enforced (`IsExpired` predicate check), and `ConsumedAt` is terminal (one-time use). After expiry or consumption, no valid operation remains in-flight. Grace adds complexity without protection. Shipped without it.

**3. No Options Class:**
Mirror borrowed `ViettelMysignOptions` for grace-minute config. We have no grace → 0 new configuration surface. Timer period (60min) is sensible default; if future tuning needed, it's a 5-min code change vs. a 1h config ceremony.

## Architectural Debt & Dependencies

**RLS All-Tenant Sweep:**
Job relies on db_owner connection; RLS is DB-level, no EF query filter on IdentityDbContext. Under future least-privilege RLS rollout, this scopes to ambient tenant. Documented carryover with `SIGN-PENDINGTX-CLEANUP-JOB-LEAST-PRIV` (shared deferred task). No block; cleanup is admin-tier anyway.

## Next Steps

- Await user approval for commit + push
- Backlog row RESOLVED + docs synced (dependency-map, changelog)
- No follow-up work unless grace-period reconsideration surfaces in production (unlikely given terminal nonce semantics)

---

**Lessons for Future Cleanup Jobs:**

1. **Set-based delete vs. load-mutate:** only load-mutate if you need entity events, audit side-effects, or conditional cascades. Otherwise delete in-place.
2. **Grace period rationale must anchor to live operation risk.** Not all cleanup needs buffer. Write the in-flight scenario first; if none exists, cut grace.
3. **All-tenant sweep via db_owner is RLS debt.** Document carryover task + tie to least-priv audit. Do not pretend it's "secure by default."
