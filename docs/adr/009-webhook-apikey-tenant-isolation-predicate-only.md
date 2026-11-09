# ADR-009: Webhook & API-Key Tenant Isolation — Predicate-Only (RLS Rejected)

**Date:** 2026-06-21
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

An architecture/DI audit flagged that the two shared-infrastructure DbContexts
`WebhookDbContext` (schema `webhooks`) and `ApiKeyDbContext` (schema `gateway`) own
**tenant-scoped tables** — `webhooks.subscriptions`, `gateway.ApiKeys`,
`gateway.ApiKeyScopes` all carry a `TenantId` column — yet, unlike the 15 module
DbContexts, they have **no SQL Row-Level Security (RLS) policy** applied (the finding
labelled this MED). The audit's first instinct was "add RLS to these tables too" for
defense-in-depth parity with the module tables (`RlsMigrationHelper` covers ~36 policies).

A red-team review (C1, Critical) rejected that direction. This ADR records **why
predicate-only isolation is the correct architecture for these two surfaces**, not a
gap — so future audits cite this instead of re-opening it, and so a future engineer does
not "restore parity" by adding an RLS migration that silently breaks production.

## Why RLS is infeasible here (lookup-before-tenant + background read)

The RLS predicate (`RlsMigrationHelper.cs:57-70`) returns a row only when
`IS_MEMBER('db_owner') = 1` **or** `@TenantId = SESSION_CONTEXT('TenantId')`. It depends on
the tenant being known and pushed into `SESSION_CONTEXT` *before* the query runs. Both of
these surfaces violate that precondition by design:

1. **API-key validation is lookup-*before*-tenant.** `ApiKeyService.ValidateAsync`
   (`ApiKeyService.cs:125-147`) resolves a key by its `Prefix` —
   `db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix)` — and only *then* learns the
   tenant from the matched record. There is no tenant to set in `SESSION_CONTEXT` before the
   read (chicken-and-egg). Under a least-privilege runtime login, RLS would filter that
   lookup to **zero rows → every API key rejected → all M2M auth dead.**

2. **Webhook delivery reads in a background job with no tenant context.**
   `WebhookDeliveryJob.ExecuteAsync` (`WebhookDeliveryJob.cs:41-49`) runs inside Hangfire
   (`[AutomaticRetry]`, never on an HTTP request thread) and loads the subscription by primary
   key: `db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.IsActive)`.
   A background job has no ambient `TenantId` to push into `SESSION_CONTEXT`, so RLS would
   return **zero rows → silent delivery failure** (the job logs "subscription not found" and
   returns without exception, retry, or alert — the worst failure shape).

3. **Dev/CI would not catch it.** Dev and integration tests connect as `sa`/`db_owner`, which
   satisfies the predicate's carveout. RLS-on-these-tables would be **green in dev and broken
   in prod** — exactly the false-green class this codebase guards against.

## The decision is robust to the open question

It does not matter whether the production runtime login is *already* non-`db_owner` or still
mid-rollout (`deployment-guide.md` §"Application DB Principal", lines 262-271, states the
hard requirement; the staged `db_ddladmin`→least-privilege cutover is lines 249-254). If it is still `db_owner` today, RLS on these
tables is silently bypassed via the carveout (so adding it would appear harmless); the moment
the security hardening completes and drops `db_owner`, that RLS **detonates** — webhook
delivery and M2M auth break together. Adding RLS now therefore *plants a latent landmine timed
to the completion of the tenant-isolation hardening itself.* Predicate-only is correct now and
after cutover.

## Decision

1. **`webhooks.subscriptions`, `gateway.ApiKeys`, `gateway.ApiKeyScopes` use explicit
   `WHERE TenantId = …` predicate isolation, NOT RLS.** Tenant scoping lives in the query
   layer (admin/dispatcher paths filter by `TenantId`; unique/indexed by `TenantId`), backed
   by high-entropy secrets (API key = `SHA-256(aqt_{prefix}_{random24})`; webhook secret =
   HMAC-SHA256 hash) so a leaked row id is not directly exploitable.

2. **No RLS migration is added to these two contexts.** `WebhookDeliveryJob` and
   `ApiKeyAuthHandler`/`ApiKeyService` are left untouched (zero regression risk).

3. **A registry-based architecture guard backstops the *future*.** A new shared-infra table
   that carries `TenantId` must either gain an RLS policy or be consciously added to a
   predicate-only allowlist (with a documented reason referencing this ADR). The guard uses an
   **explicit allowlist keyed by entity type**, NOT a column-name heuristic — column heuristics
   would false-positive on system-wide infra tables (Messaging/Backup/Alerting) that are *not*
   tenant-scoped. See `SharedInfraTenantTableIsolationGuardTests`.

## Tenant-scoped vs system-wide shared-infra tables (the registry)

| Table | Context | TenantId? | Isolation |
|-------|---------|-----------|-----------|
| `webhooks.subscriptions` | `WebhookDbContext` | yes | **predicate-only (allowlisted)** — background delivery read, no tenant ctx |
| `gateway.ApiKeys` | `ApiKeyDbContext` | yes | **predicate-only (allowlisted)** — lookup-before-tenant by prefix |
| `gateway.ApiKeyScopes` | `ApiKeyDbContext` | yes | **predicate-only (allowlisted)** — child of ApiKeys, same access path |
| `webhooks.delivery_attempts` | `WebhookDbContext` | **no** | scoped indirectly via `SubscriptionId` (not tenant-scoped) |
| Messaging / Backup / Alerting infra | (various) | **no** | system-wide, not tenant-scoped → out of scope for this guard |

**Guard coverage limits (no current violation):** the arch guard discovers tenant-scoped tables by
walking `DbSet<T>` properties for a `Guid TenantId`. An EF *owned type* / navigation-only entity
(no `DbSet`) or a *non-`Guid`* tenant key would escape detection. None exist today; a future change
of either shape must extend discovery (`context.Model.GetEntityTypes()`) rather than rely on the
`DbSet` walk.

## Consequences

### Positive
- Webhook delivery and M2M auth keep working under the least-privilege runtime login.
- The "add RLS for parity" instinct is documented as rejected with its failure mode, so it is
  not re-litigated.
- A future tenant-scoped shared-infra table cannot silently ship without RLS *or* a conscious
  predicate-only declaration — the guard fails the build.

### Negative
- These three tables have no engine-level (DB) tenant backstop; isolation relies on the query
  layer always including the `TenantId` predicate. Mitigation: the predicate paths are narrow
  and covered; high-entropy secrets mean a row id alone is not exploitable. A heavier DB
  backstop is possible only via the audit module's asymmetric NULL-tenant pattern
  (`RlsMigrationHelper.NullableTenant`) plus an opt-in delivery-path session-context set —
  a separate, larger effort outside this finding's scope.

## Invariant

Every entity exposed by a shared-infrastructure `DbContext` (in `AqtCoreFW.Infrastructure`)
that carries a `TenantId` property MUST either have an RLS policy or appear in the
predicate-only allowlist with a documented reason. Enforced by
`SharedInfraTenantTableIsolationGuardTests`.

## Related

- `src/shared/AqtCoreFW.Infrastructure/Webhooks/WebhookDeliveryJob.cs`,
  `WebhookDbContext.cs`
- `src/shared/AqtCoreFW.Infrastructure/ApiKeys/ApiKeyService.cs`, `ApiKeyDbContext.cs`
- `src/shared/AqtCoreFW.Infrastructure/Persistence/RlsMigrationHelper.cs` (predicate carveout)
- `docs/deployment-guide.md` §"Application DB Principal" (least-privilege runtime login)
- `tests/architecture/AqtCoreFW.Architecture.Tests/SharedInfraTenantTableIsolationGuardTests.cs`
- ADR-003 (SQL Server per-module schema)
