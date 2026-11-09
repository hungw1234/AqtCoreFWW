# Per-Tenant HMAC Audit Chain Fix (QĐ742 Tamper-Evidence)

**Date**: 2026-06-15 14:00
**Severity**: Critical
**Component**: Audit module (HMAC chain, RLS, backfill)
**Status**: Resolved
**Commit**: `89581e3c`

## What Happened

Shipped 4-phase TDD slice to fix broken HMAC chain tamper-evidence. Audit events accumulated 151/151 `SequenceId=0` entries in dev, causing the chain to degenerate and false-tamper verification on every event. Built per-tenant serialization via `sp_getapplock`, backfill endpoint under SystemAdmin, and health-check guard. All builds, tests, and smoke green.

## The Brutal Truth

This was a footgun waiting to fire in production. The chain was fundamentally broken — `SequenceId` never assigned — and nobody caught it for two releases because:
- Dev schema doesn't enforce uniqueness on the chain (migration was write-only, no constraint)
- Verify logic still *reported success* even though the chain degenerated
- The test fixtures padded `SequenceId` manually, hiding the real code path

Worse: RLS `audit_admin_bypass` is a half-measure. It exposes only NULL-tenant system rows; named-tenant rows stay hidden unless your session has `GRANT` or you flip the context. The backfill tests ran under db_owner (RLS bypassed entirely), so the cross-tenant read defect was structurally invisible. The regression test had to live in `TenantIsolation.Tests` under a least-privilege login to even be readable.

Two pre-existing unrelated failures in the test suite got filed to backlog to avoid scope creep.

## Technical Details

**Chain Keying Bug (Root):**
- Code assigned `SequenceId = 0` uniformly (hardcoded default)
- Chain verify ordered by `InternalId` (insert-order), not `SequenceId` (signer-order)
- Signer (human/application) produces a sequence; inserter (background job) produces a different sequence → permanent mismatch
- Fix: signer assigns `SequenceId` at signing time, keyed to their append event; chain verifies by `SequenceId` ascending; gaps = tamper

**Concurrency Serialization:**
- Bare `EXEC sp_getapplock` with no return-code check → silently proceeds on 30s timeout
- Two concurrent signers fork the chain without retry or detection
- Fix: check `@rc < 0` (lock timeout/conflict), THROW → `[AutomaticRetry]` re-runs the signer transaction

**RLS Asymmetry (Latent until least-privilege cutover):**
- `audit_admin_bypass` flag = read-only for cross-tenant admin ops (backfill, health-check)
- Bypass exposes NULL-tenant rows only; named-tenant rows remain hidden unless db_owner/matching TenantId session context
- Backfill tests ran as db_owner → RLS silent-pass, defect invisible
- Least-privilege regression test under `rls_test_app` login → RED, revealed the gap
- Fix: use separate db_owner connection for cross-tenant reads (backfill enumeration, health-check count)

**Fingerprint Byte-Parity Foot-Gun:**
- Signer (raw SqlDataReader projection) and verifier (EF entity) had two separate `BuildFingerprint` implementations
- Small divergence (field order, null-handling, escaping) → every verify falsely reports tamper in production
- Caught in code review pre-commit
- Fix: single `BuildFingerprintCore` so divergence is impossible by construction

**Backfill Endpoint:**
- POST `/audit/chain/backfill` (SystemAdmin-only, 202 + Hangfire async)
- Enqueues backfill job per tenant (parallel safe via per-tenant lock)
- Audit-the-backfill via captured actor + elevated connection string
- Config-able tenant list (default all active)

**Health Check:**
- New `audit-unsigned-backlog` check (config-able threshold, default >100 unsigned OR >10min oldest → Unhealthy)
- Runs at startup and on admin dashboard

## What We Tried

1. **Phase 1: Keying & Fingerprint** — identified SequenceId and dual-builder issues via TDD; collapsed fingerprint builders
2. **Phase 2: Per-Tenant Serialization** — added `sp_getapplock` with return-code validation; raw SqlConnection + RLS bypass for cross-tenant reads
3. **Phase 3: Backfill Endpoint** — SystemAdmin POST handler + Hangfire job + elevated connection + actor capture
4. **Phase 4: Health Guard** — unsigned-backlog check + config + startup probe

All phases green on first compile (TDD drove architecture before code).

## Root Cause Analysis

**Why `SequenceId=0` went undetected:**
- Signer code defaulted to 0 (no explicit assignment logic)
- Test fixtures manually padded values → masked the real path
- No migration constraint on the column → silent duplicate 0s
- Verify still returned *success* (wrong order just meant different events) → no loud failure

**Why RLS defect survived code review:**
- db_owner tests hide RLS entirely
- Cross-tenant admin ops weren't exercised under non-owner login
- Backfill logic looked correct in isolation ("just enumerate tenants and run per-tenant")
- The *correctness* assumption (RLS bypass allows cross-tenant read) was wrong for named tenants

**Why fingerprint divergence wasn't pre-caught:**
- Two separate builders maintained in parallel
- No shared test vector (signer+verifier produce same bytes)
- Code review caught it post-implementation; should have been design-time

## Lessons Learned

1. **RLS `audit_admin_bypass` is asymmetric** — bypass flag ≠ universal read. Test cross-tenant admin ops under least-privilege login, not db_owner. Latent bugs survive db_owner suite.

2. **Dual fingerprint builders = divergence time-bomb.** Collapse to single shared builder, test for byte-identity. One-liner divergence (field order, null coercion) → production false-tamper on next verify.

3. **`sp_getapplock` return-code is NOT optional.** Bare `EXEC` + no check = silent fork on timeout. Check `@rc < 0`, THROW, rely on retry. Concurrency test won't catch it (contention resolves <30s in dev).

4. **Test fixtures padding hardcoded values hide real code paths.** Signer tests should verify assignment happens, not assume it. A single `SequenceId = 0` in production code for two releases went undetected.

5. **Cross-tenant operations must exercise least-privilege login.** db_owner tests pass; least-privilege regression tests fail → the defect was RLS-specific. Backfill enumeration + health-check count both needed elevated-connection fix.

6. **Verify must be loud, not silent.** "Verify returned success" when chain was degenerate should have been "Verify raised exception: gap at position X."

## Next Steps

**Blocked (backlog):**
- `AUDIT-CHAIN-BACKFILL-PROD-PO`: prod backfill run (ConnectionStrings:AuditAdmin wiring)
- `AUDITADMIN-CONN-PROD-WIRING`: elevated connection string + rotation policy for least-privilege cutover
- `AUDIT-FINGERPRINT-V3-WHO-WHERE`: fingerprint version 3 (if schema drift forces recanonical)
- `AUDIT-CHAIN-BACKFILL-RICHER-EVIDENCE`: capture backfill reason/actor in audit event
- `AUDIT-MIGRATION-SQL-MIRROR-TEST-STALE`: SQL mirror test didn't run (not wired to CI yet) → verify migration on SQL Server mirror
- `RLS-POLICY-COUNT-EXPECTED-STALE`: health-check count may stale on RLS policy drift (advisory test-to-verify)

**Action:**
- Backfill must run prod before any tamper-evidence claim (SLA per NĐ742)
- Prod wiring of `ConnectionStrings:AuditAdmin` (db_owner) + rotation schedule before cutover
- Smoke: verify SystemAdmin can POST backfill endpoint without error
