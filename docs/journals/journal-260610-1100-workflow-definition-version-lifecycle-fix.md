# Journal — Workflow Definition Version Lifecycle Fix (2026-06-10 ~11:00)

**Slice:** `/cook WF-CLONE-AFTER-DELETED-VERSION-500` (interactive) · commit `375d682f` · 3rd ship of the day.

## What shipped

Two SQL-verified version-lifecycle defects on workflow definitions, fixed with no migration (user chose Option A at the design gate):

1. **Clone version collision → 500.** `Clone()` set `Version = source.Version + 1`. The `(TenantId, DefinitionKey, Version)` unique index is `.IsUnique()` with no IsDeleted filter, so a soft-deleted version still occupies its number. Cloning v1 when a soft-deleted v2 existed recomputed 2 → unique collision → 500. Fix: `Clone` now takes an explicit `nextVersion = MAX(version) over all rows incl soft-deleted + 1` (new `GetMaxVersionByKeyAsync`, `IgnoreQueryFilters`, tenant-scoped).
2. **Delete leaves no latest.** Delete only soft-deleted; deleting the latest version stranded the key with zero latest rows. Fix: after soft-delete, promote the highest surviving (non-deleted) version to `IsLatest`, wrapped with the delete in one `TransactionScope` (new `MarkLatest` + `GetHighestSurvivingByKeyAsync`).

## Design decision (the one real fork)

Bug B had two viable fixes: (A) clone computes MAX(incl soft-deleted)+1 — no migration, versions monotonic-never-reused; (B) change the unique index to filter `IsDeleted=0` — migration, allows version reuse. Surfaced via AskUserQuestion; user picked A. A is the KISS choice: no schema change, versions stay monotonic (a deleted v2 means the next clone is v3, never a recycled 2), and it respects the index exactly as it stands.

## Lessons (reusable)

- **A unique index that doesn't filter soft-deleted makes "version = N+1" wrong.** Any version/sequence calc against a soft-delete table must count deleted rows if the uniqueness constraint does. `IgnoreQueryFilters` (scoped to an explicit tenant) is the right tool — bypass the soft-delete filter for the MAX, keep the tenant predicate explicit so there's no cross-tenant leak.
- **Soft-delete + a "promote survivor" step must be one transaction.** A crash between them strands the aggregate (here: a key with no latest row). Reused the clone handler's ambient `TransactionScope` precedent — repo `SaveChanges` auto-enlists.
- **Make the corrected invariant a compile-time requirement.** Changing `Clone()` to *require* `nextVersion` (rather than computing `+1` internally) forces every future caller to supply the right number — the bug can't silently come back via a new caller.
- **The partial index `[IsLatest]=1 AND [IsDeleted]=0` is why the handoff is clean:** the deleted row drops out of the latest-index regardless of its lingering IsLatest flag, so promoting the survivor never transiently double-counts latest rows. Worth knowing before anyone adds a "exactly one latest" DB constraint.
- **Dev-mode Vault loses all secrets on container restart** (in-memory backend) — re-seed via `vault-init` before the stack will boot. Hit this again at slice start.
- **Filtered-index UPDATE needs `SET QUOTED_IDENTIFIER ON`** in sqlcmd, else "incorrect SET options" error — relevant for the dev-DB seed repair.

## Verification

- Workflow Application + Infrastructure build 0W/0E.
- 7/7 integration: 3 new version-lifecycle facts (clone-after-soft-deleted→v3 no-500; delete-latest→promotes survivor; delete-non-latest→untouched) + 3 existing clone deep-copy + existing delete-by-PublicId. All 3 new facts fail pre-fix.
- code-review APPROVE 0C/0H/0M (1 Low applied — concurrent-clone race comment; the race is pre-existing and the unique index is the correct serializer for an admin-rare op).
- Dev-DB seed repaired: `Demo Parallel Process` v1 (Id 10003) flipped back to IsLatest=1 (already IsActive=1); broken v2 (10004) stays soft-deleted, IsLatest=0. Cloning the seed works again.

## Carry

- Push pending (committed local `375d682f`).
- 2 `E2E Parallel` orphan pairs (Ids 10005-10008) still in dev DB — spec cleanup silently failed; harmless, not filed.
- API restarted at finalize to restore the dev stack.
