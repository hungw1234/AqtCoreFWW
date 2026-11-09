# Journal — DocFlow orphan signed-blob sweep

**Date:** 2026-06-16 10:15 ICT · **Slice:** `SIGN-DOCFLOW-ORPHAN-BLOB-SWEEP` (backlog T4) · **Mode:** `/cook` · **Type:** feature (BE, Files + Api host)

## What

A daily Hangfire job that reclaims orphaned signed-PDF blobs from the `signing-docflow` bucket,
plus the storage-list capability it needs. No change to the hot upload path.

## Audit-at-slice-start reframed the row (INVARIANT 260522-A)

The row read "add a sweep for unreferenced signed-blobs (T4)". Two empirical findings reshaped it:

1. **The orphan key is never persisted.** At `FileUploadHandlerController.cs:272` the signed blob is
   uploaded under `{tenantId}/{docId}/{guid}.signed.pdf` *before* `record.Complete()` commits. When
   that commit loses the rowversion race (or the process dies), `SignedStorageKey` stays null — the
   key existed only in the losing request's memory. So a DB-only sweep is impossible; the orphan can
   ONLY be found by enumerating the bucket.
2. **`IFileStorageService` had no list method** (Upload/Download/Delete only). So "add a sweep"
   necessarily means extending the shared storage contract — materially bigger than the T4 label.

Surfaced this to the user (genuine scope/risk fork, not a grep-answerable question) with three
options: A additive sweep (extend the contract, enumerate the bucket), B prevention/self-heal
(persist the intended key pre-upload via a migration → DB-only sweep, but touch the shipped hot
path), C defer + runbook. User chose **A**.

## Key decisions

- **Per-tenant prefix scoping kills the cross-tenant footgun.** Blob keys are tenant-prefixed. If the
  sweep listed the whole bucket but only loaded one tenant's referenced keys, it would delete other
  tenants' valid blobs. The job groups objects by their `{tenantId}/` prefix and reconciles one
  tenant at a time — the prefix *is* the tenant boundary, so no cross-tenant elevation is needed.
- **Per-tenant SESSION_CONTEXT pin = least-priv correctness.** Background jobs carry no tenant
  context. Under a future non-db_owner runtime, the RLS predicate
  (`IS_MEMBER('db_owner')=1 OR @TenantId = TRY_CAST(SESSION_CONTEXT('TenantId')…)`) would return ZERO
  rows for an unscoped read → referenced-set empty → every blob looks orphan → catastrophe. The job
  pins SESSION_CONTEXT per tenant before the read (mirroring the verified
  `FileUploadHandlerController.SetTenantSessionContextAsync`, same held-open connection), so RLS
  returns the right set. Today's db_owner runtime bypasses RLS, so it is correct on both sides of the
  least-priv cutover. This directly applies the audit-admin RLS-asymmetry lesson.
- **Host placement.** The job needs both `IdentityDbContext` and `IFileStorageService`;
  Identity.Infrastructure does not (and per module-isolation should not) reference Files.Domain. The
  Api host is the one assembly that legitimately references both — exactly where
  `FileUploadHandlerController` already lives. No new cross-module dependency.
- **Default queue, no new constant.** Recurring jobs run on `default` (drained per `opt.Queues`), so
  no `BackgroundJobQueues` constant was added and the queue-registration arch guard is untouched.

## Review-driven hardening (both Medium findings applied)

Code-review returned DONE_WITH_CONCERNS, no Critical/High; deletion-safety traced sound. Applied both:

1. **Empty-referenced circuit-breaker.** Even with the session-context pin correct today, the
   no-throw empty-read path is fragile to a future connection/pooling refactor that breaks the
   same-connection guarantee — and the blast radius is *permanent deletion*. So: if a tenant has
   deletion candidates but zero referenced keys, skip-and-warn instead of deleting. A genuinely
   100%-orphan tenant is rare and is left for an operator to reclaim from the warning; that trade is
   correct when the alternative is silent mass-deletion. The threat is not abstract — the least-priv
   cutover is a tracked, pending event (`SIGN-DOCFLOW-LEASTPRIV-SESSIONCTX`).
2. **`[AutomaticRetry(Attempts=0)]`** for parity with `DelegationExpiryJob` (deletes are idempotent;
   avoids a redundant full-bucket re-list on retry).

## Coverage boundary

The pure orphan-selection rule (`SelectOrphanKeys`: age cutoff + reference check + null-timestamp
kept) is unit-tested exhaustively (5 cases, no fixture). The orchestration (bucket list → group →
per-tenant session-context → IgnoreQueryFilters read → circuit-breaker → delete) is thin and mirrors
verified patterns; its DB/RLS behaviour is best exercised by a live Testcontainers+MinIO test, noted
as a possible follow-up rather than built for a T4.

## Verification

- Files.Infrastructure 0W/0E; Api host 0W/0E (TreatWarningsAsErrors).
- 5 sweep tests + Files.Infrastructure 37 + Files.Application 114 + Architecture 109 = green.
  Interface change is additive — only the MinIO impl was touched; all other consumers are mocks.

## Lesson

"Add a sweep for unreferenced X" is only DB-only when X's identity is persisted. Here the orphan's
key dies with the failed request, so reclamation needs storage enumeration — which exposed that the
storage abstraction had no list method. Trace where the orphaned identifier actually lives before
assuming the cleanup can query for it. And for any cross-tenant batch job whose action is deletion,
the safety question is "what happens when the reference-set loads empty?" — answer it with a
circuit-breaker, not just a try/catch, because the dangerous case throws nothing.
