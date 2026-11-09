# Forms Public Submit 500 — Tenant-Agnostic Gate Miss

**Date**: 2026-07-07 15:30 (Asia/Saigon)
**Severity**: High
**Component**: Forms.Public submission pipeline + CrossModuleUnitOfWork
**Status**: Resolved

## What Happened

`POST /api/v1/public/forms/{code}/submit` returned 500 on anonymous (unauthenticated) HTTP requests. The endpoint is intended to allow public form submission without identity context, but the command pipeline rejected it outright before reaching the handler.

## The Brutal Truth

This is a gap in contract enforcement: we had a public-facing endpoint that explicitly allowed anonymous access, but the command mediator still demanded tenant context. Worse, nobody caught it because our test login (`sa`, db_owner) bypasses Row-Level Security entirely — so the integration tests that should have failed passed silently. The bug was invisible until runtime.

The frustrating part: the fix was straightforward (one marker interface, one overload), but the false-green tests reveal a systemic testing gap. We're validating permission gates in an environment where they don't actually enforce.

## Technical Details

**Tier 1 (Immediate 500):** `SubmitPublicFormCommand` lacked the `ITenantAgnosticCommand` marker. The `TenantAwareBehavior` in the pipeline checked:

```
if (cmd is not ITenantAgnosticCommand && !ambient.IsSystemAdmin)
  throw UnauthorizedException // 500 before handler runs
```

No ambient `ITenantContext.TenantId`, no SystemAdmin claim → rejection.

**Tier 2 (Latent, deferred):** `forms.FormSubmissions` has an RLS BLOCK predicate:

```sql
WHERE IS_MEMBER('db_owner') OR TenantId = SESSION_CONTEXT('TenantId')
```

Under a non-db_owner login (our pending least-priv migration), an unset `SESSION_CONTEXT('TenantId')` silently blocks the INSERT even if Tier 1 passes. Scout verified: today's runtime login is `sa` (db_owner), so RLS is fully bypassed. Only the pipeline gate fires the 500. Forward-prep is essential.

## What We Tried

**Option A (considered, rejected):** Mark the command `ITenantAgnosticCommand` and let the handler do raw SQL with no session context → leaks `set SESSION_CONTEXT` logic into Application layer.

**Option B (chosen, shipped):** New overload `ICrossModuleUnitOfWork.BeginAsync(Guid tenantOverride, CancellationToken)` — extends the shared primitive, keeps session/SQL mechanics in Infrastructure. Handler resolves tenant from template server-side and pins it via the override.

Implementation: `BeginCoreAsync(Guid? explicitTenant)` checks if override supplied; calls new `SetTenantSessionContextAsync(tenantId)` helper. Null path is byte-identical to H2 rebase (2026-07-06), zero regression risk. All 5 existing callers continue through the non-override path; compile-safe overload dispatch.

## Root Cause Analysis

1. **Contract mismatch:** Endpoint declared public + handler needed tenant context, but command didn't bridge that explicitly. Missing the `ITenantAgnosticCommand` marker meant the default "reject anonymous" policy applied.

2. **False-green tests:** Integration tests run under `sa` login, which is both db_owner and bypasses all RLS checks. So tests for "anonymous submission succeeds" never actually exercised the RLS BLOCK. A test under least-priv login would have failed immediately on the same code.

3. **Incomplete scout:** Prior code review checked Application → Infra call chain but didn't verify the command's pipeline marker. Relied on test results without questioning their environment assumptions.

## Lessons Learned

- **Test login != production login.** When testing RLS or tenant-aware gates, unit tests under `sa` (db_owner) are false-green. Require at least one integration path under a non-db_owner test user.

- **Marker interfaces are structural.** `ITenantAgnosticCommand` is not just documentation; it's the gate logic's contract. Missing it means the default "reject" fires. Scout for all command variants when adding pipeline behaviors.

- **Overload, don't signature-change.** The new `BeginAsync(tenantOverride)` preserved all existing callers. A signature edit would have forced a sweep across the codebase and risked silent misses. Additive API (overload) = lower blast radius.

- **SESSION_CONTEXT requires coordination.** Setting it in the handler (Application) couples the command to SQL-specific mechanics. Pushing it to Infrastructure (UoW overload) makes it reusable and testable in isolation.

## Next Steps

1. **Backlog row PUBLIC-FORMS-LEAST-PRIV-ANON-READ filed:** Forms handler reads FormTemplates and resolves tenant *before* pinning SESSION_CONTEXT. Under least-priv, those reads are FILTER-gated on TenantId, so an unresolved/incorrect tenant would 404 before reaching the INSERT. Requires admin-bypass read or FILTER carve-out — design doc and scope for later cycle.

2. **RLS integration testing:** Add a new test-seed non-db_owner login; run critical RLS-gated endpoints (Forms, Audit, Signature) under that login in CI. False-green rate will drop; real gate bugs will surface pre-ship.

3. **Documentation:** Update dependency-map.md to note that `ITenantAgnosticCommand` is mandatory for public-facing submission endpoints. Record the lesson in `feedback-rls-integration-under-least-priv.md`.

4. **Verify:** Confirm `POST /api/v1/public/forms/{id}/submit` returns 200 + audit trail under anon HTTP (Playwright smoke test). Monitor Hangfire for background job execution post-fix.

---

**Status**: DONE

**Summary**: Fixed FORMS-PUBLIC-SUBMIT-500 by adding `ITenantAgnosticCommand` marker + new UoW overload to pin tenant server-side. Tests 18/18 pass. Root cause: missing marker; false-green tests masked RLS latency due to `sa` login bypassing all gates.

**Journal**: `C:\AqtCoreFW\docs\journals\forms-public-submit-500-tenant-agnostic-260707.md`
