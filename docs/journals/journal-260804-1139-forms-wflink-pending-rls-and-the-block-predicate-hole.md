# Journal — RLS on the orphan-attempts table, and the BLOCK predicate that armed a silent hole

**Date:** 2026-08-04 · **Branch:** main · Backlog `FORMS-WFLINK-PENDING-TABLE-NO-DB-RLS`.

## What shipped

- **Migration `20260804030737_AddWorkflowLinkPendingAttemptRls`** — adds the standard tenant RLS policy
  (FILTER + BLOCK-AFTER-INSERT) to `forms.FormSubmissionWorkflowLinkPendingAttempts`, which had none. It
  landed after the Phase-06 RLS-restore migration, so it was the only tenant-scoped Forms child table
  without a policy — a gap, not a design choice (every sibling incl. `FormTemplateVersions` has one).
- **`RlsPolicyPresenceTests`** matrix +1 (50→51, forms 4→5).
- **`FormWorkflowLinkPendingRlsEnforcementTests`** — 3 tests via a non-db_owner constrained login:
  FILTER (cross-tenant read blocked), BLOCK (foreign-tenant INSERT → 33504), same-tenant INSERT succeeds.
- **Fixture GRANT** — the test's constrained login needed table-level DML on the new table (prod is
  unaffected: `aqtcore_nonaudit` grants `ON SCHEMA::[forms]`).
- **Handler fix** (the real content) — see below.

## The finding that made this more than a one-line migration

Code-review (focused on write-path regression per `feedback-fixing-a-bug-can-arm-a-dormant-hole`) traced
a real silent regression the BLOCK predicate would arm:

- The retry job pins SESSION_CONTEXT per row (safe). The db_owner backfill bypasses (safe). But the
  **event handler** that writes the FIRST orphan row runs AFTER the submit command commits its own UoW,
  then `AddAsync` opens the DbContext's OWN pooled connection. On the **anonymous public-form** path the
  ambient `ITenantContext.TenantId` is null → the interceptor skips `sp_set_session_context` → SESSION_CONTEXT
  is unset → the new BLOCK-AFTER-INSERT predicate rejects the INSERT with error 33504 → the handler's
  fail-soft catch swallows it. Net: on the public path, orphan-tracking rows would silently stop persisting
  the moment the migration deployed. The authenticated path was safe (tenant from the auth claim).
- The green test suite could not see it: the behavioral coverage runs through `WebAppFixture` as
  **db_owner**, which the predicate bypasses (`IS_MEMBER('db_owner')=1`) — the `feedback-sa-dbowner-false-greens-rls`
  pattern. Only a non-db_owner probe on the public path would have caught it.

## The fix (user chose: handler self-pins)

The handler now opens its OWN tenant-pinned UoW via `ICrossModuleUnitOfWork.BeginAsync(notification.TenantId)`
— the interface's explicit-tenant overload is documented for exactly this "[AllowAnonymous] public-form
submission whose tenant comes from the template row" case. Two subtleties:

1. The read had to switch from the non-enlisting `GetBySubmissionIdAsync` (NoTracking) to the enlisting
   `GetTrackedBySubmissionIdAsync` — otherwise the read opens the context's connection first and the
   later `EnlistAsync` (which needs a closed connection) fails for the INSERT.
2. Only two publishers exist (`SubmitFormCommandHandler`, `SubmitPublicFormCommandHandler`), both publish
   AFTER committing their UoW, so the handler always finds the UoW inactive → `BeginAsync` never throws
   "already active".

A unit guard now asserts the handler calls `BeginAsync(TenantId)` + `CommitAsync` (and `RollbackAsync` on
failure). Combined with the non-db_owner enforcement test proving a same-tenant INSERT passes the BLOCK
predicate, the mechanism is covered in two parts; a full anonymous-public E2E under non-db_owner was
judged YAGNI.

## Verification

- Code-review: DONE_WITH_CONCERNS → 1 critical (C1, the hole above) + 3 minor, all resolved.
- Tests: Forms Application unit 363/363, Forms integration 52/52, RLS enforcement 3/3, presence 2/2,
  full TenantIsolation suite 59/59 — all green, no regression.

## Lesson reinforced

Adding a correct security control (BLOCK predicate) armed a latent SESSION_CONTEXT gap on one write path,
hidden by fail-soft + a db_owner test suite. The guard against this class is (1) ask "what silently
succeeds today BECAUSE this control is absent" before adding it, and (2) test RLS enforcement under a
non-db_owner login — a db_owner run is structurally blind to it.
