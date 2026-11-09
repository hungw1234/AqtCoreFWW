# Shipped: Signing Cleanup Jobs Least-Priv / Per-Tenant RLS Scope

**Date**: 2026-07-02 09:31  
**Severity**: High  
**Component**: Identity.SigningCertificates (background jobs, RLS enforcement)  
**Status**: Resolved

## What Happened

Shipped `SIGN-PENDINGTX-CLEANUP-JOB-LEAST-PRIV` (T3): two background jobs (`PendingTransactionCleanupJob`, `EnrollmentNonceCleanupJob`) ran tenant-wide sweeps with **zero SESSION_CONTEXT set**. Target tables enforce RLS: `IS_MEMBER('db_owner')=1 OR TenantId=SESSION_CONTEXT('TenantId')`. Under future least-priv DB logins (non-db_owner), sweep would match zero rows → silent orphan accumulation. Masked by dev-mode db_owner bypass.

## The Brutal Truth

RLS is **enforced asymmetrically**: request path has session context pinned; background jobs lost in the void. We shipped a time-bomb: switching to least-priv prod login breaks cleanup silently (no error, rows just... stay). Orphaned transactions + nonces = eventual storage/performance cliff.

## Technical Details

**Tables affected:**  
- `identity.SigningPendingTransactions` (RLS policy: TenantId filter)  
- `identity.SignerEnrollmentNonces` (RLS policy: TenantId filter)

**Root issue:** IHostedService DbContext query had no `SESSION_CONTEXT('TenantId')` → under least-priv, WHERE clause `TenantId = SESSION_CONTEXT('TenantId')` evaluates to `TenantId = NULL` → zero matches.

## What We Tried

**Option A (rejected):** Revert RLS → allowlist on both tables. Scout revealed true blast radius: migration + 2× arch-guard allowlist rules + RLS matrix drift + reduces request-path DB isolation on PKI tables. User approved Option B after evidence.

**Option B (shipped):** New helper `TenantScopedSweep`: enumerate `identity.Tenants` (non-RLS registry) → iterate per-tenant + pin `DocFlowTenantSession.PinAsync(SESSION_CONTEXT)` + run job's sweep callback with explicit `tenantId` predicate. Poison-tenant try/catch (log+continue).

## Root Cause Analysis

**KEY INSIGHT #1:** Conflated orchestration (background job runs globally) with enforcement (RLS per-tenant). The unlock: `Tenants` registry is **NOT RLS-protected** → enumerable under least-priv. Enumerate the tenant universe from a **non-RLS registry, never from RLS-protected target table** (lesson from audit HmacChain backfill, which self-enumerated DISTINCT TenantId under bypass → failed at real least-priv).

**KEY INSIGHT #2:** `TenantSessionContextInterceptor` no-ops when ambient tenant is null (background job), but manually pinned `SESSION_CONTEXT` survives on explicitly-opened connection (proven DocFlow anonymous-callback pattern).

## Traps & Lessons

1. **Test-compat:** Existing tests seeded orphan `tenantId: Guid.NewGuid()` (no Tenants registry row) → per-tenant enumeration skipped them. Fixed by seeding real Tenant rows + multi-tenant iteration tests (9/9 cleanup tests PASS).

2. **RLS matrix drift:** `RlsPolicyPresenceTests` declares 49 policies, omits `SignerEnrollmentNonces` (which has a real policy). Filed T4 `ARCH-RLS-POLICY-MATRIX-SIGNERNONCE-OMISSION`.

3. **Session context semantics:** Manual pin + explicit scope survive across EF queries on same connection — don't rely on ambient tenant when background.

## Next Steps

- Monitor cleanup job logs for poison-tenant edge cases (code-reviewer approved N1 try/catch).
- Close T4 RLS matrix omission (separate task, not blocking).
- Document invariant: "every scoped TenantId must exist in Tenants registry" (code comment added, N2 applied).

**Review outcome:** APPROVE_WITH_NITS → N1 (poison-catch) + N2 (invariant doc) applied; N3 (scale info) noted.  
**Tests:** 9/9 cleanup + 149/149 architecture.  
**Files:** TenantScopedSweep.cs (new) + 2 jobs + nonce migration comment + 2 test files.

---

**Status**: DONE
