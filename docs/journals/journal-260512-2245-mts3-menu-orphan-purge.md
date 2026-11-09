# Journal — MTS-3: Menu seeder auto-purge of orphan rows (2026-05-12)

**Branch:** `fix/mts3-menu-orphan-purge` · **Commit:** `1d29eab6` · **PR:** [#103](https://github.com/thiennd212/AqtCoreFW/pull/103) · **Backlog item:** MTS-3 (P2)

## Problem

`E2ETestDataSeeder` seeds the production `identity.AppMenus` / `identity.MenuRolePermissions` rows that drive the FE sidebar, using **per-row idempotency** (RT-04 v2): it only INSERTs rows whose `PublicId` isn't already present. That handles *adding* menus cleanly but never *removes* them — so an entry deleted from `BuildMenuList()` left an orphan DB row that the FE kept rendering as a dead 404 link. This bit us with the `admin-overview` leaf during the 2026-05-11 manual-test session (bug #14), fixed at the time by a one-off manual `DELETE`. MTS-3 makes the seeder self-healing.

## Approach

Added `PurgeOrphanMenusAsync`, called from `SeedMenusAsync` right after the insert phase.

**Scope of deletion** — the risky part. The seeder is *not* the only writer to `AppMenus`: `CreateMenuCommandHandler` lets admins create menus through `/admin/menus`. So a blind "delete everything not in the seed list" would nuke user data. The seeder pins all its rows in the `60000000-0000-0000-0000-XXXXXXXXXXXX` family; admin-created menus use `IGuidGenerator` (Guid v7, timestamp-prefixed) and can never land in that range. So the purge filter is `TenantId == null && PublicId starts-with "60000000-0000-0000-0000-"` — exactly the rows the seeder owns. Materialize the system menus first, then filter the prefix client-side (no `Guid → string` SQL-translation worry).

**Delete ordering** — `AppMenu.ParentId` is a self-referencing FK with `DeleteBehavior.Restrict`, so a parent can't be deleted while a child still points at it. Rather than trust EF Core's batch ordering for self-references (historically flaky), the purge partitions the orphans into **deletion layers** (leaves first — rows referenced by no other orphan; then rows that become unreferenced once that layer is gone; …) and does one `SaveChanges` per layer. `MenuRolePermissions` are left to the DB's `ON DELETE CASCADE` — deliberately *not* `.Include`d, both because the cascade already handles them and because tracking them would leave stale `Deleted` entries in the shared scoped `DbContext` if a later save fails (code-review M1).

**Concurrency / failure** — the seeder is Development-only and normally single-instance, but if two replicas race the purge, the loser gets `DbUpdateConcurrencyException` ("0 rows affected") — benign, swallowed at `Debug`. Any *other* `DbUpdateException` (e.g. a desired menu still pointing at an orphan as its parent — a real structural bug) is logged at `Warning` and the remainder left for the next boot, with an after-loop `LogError` shortfall report (suppressed when the only cause was the benign race — code-review M2). The Redis `menu-tree:*` flush condition gained `|| purged > 0` so a purge-only run still invalidates the cache.

The selection predicate and the layering algorithm were extracted as pure helpers (`IsSeededMenuFamily(Guid)`, `OrderOrphanMenusLeafFirst<T>`) so the non-trivial logic is unit-testable without a DB.

## Testing

- `MenuSeederPurgeLogicTests` — 10 fast cases: the seeded-family predicate (incl. upper-case hex, out-of-family Guids, first-segment-differs) and the layering (leaves before parents, a 3-deep chain bottom-up, a mutual-parent cycle drops the cyclic rows so the caller can report the gap, empty input). **10/10 PASS.**
- `MenuSeederOrphanPurgeSmokeTests` — `[Trait("Category","Smoke")]`, end-to-end: boots the full Program host (first seeder pass migrates + seeds the canonical structure), injects three orphan rows (a parent + its child + the historical `admin-overview` PublicId) and one admin-created (non-family) menu plus a permission row on the orphan child, re-invokes the seeder, then asserts the orphans are gone (exercising the parent-before-child ordering), the permission row cascaded away, the admin-created menu survives, and a canonical menu (`dashboard`) is untouched.
- Build 0E (Api host + SmokeTests); `Architecture.Tests` 94/94.
- Code review: APPROVE_WITH_NITS, 8.5/10 — both Medium error-path findings (M1 stale-dependents, M2 over-loud `LogError`) fixed pre-commit. Report: `plans/reports/code-reviewer-260512-2215-mts3-menu-orphan-purge.md`.

## What didn't go to plan

The smoke test **can't run in this dev environment** — booting the host triggers `EmailHashedUserStore` → `VaultPepperResolver`, which gets `permission denied` from the dev Vault for the seed tenant's pepper path. This is a pre-existing condition (the whole `Startup.SmokeTests` suite has the same problem locally; see memory `feedback-vault-init-seed-all-seed-data-tenants` and the backlog's QD742-AUDIT-403 note "Startup.SmokeTests 3/4 fail"). It is *not* introduced by this change — `ApiStartupSmokeTests` fails identically — and the smoke test runs fine in CI where Vault is provisioned. Filed **MTS-3-FU1** to make the fixture env-resilient (kill the Vault pepper resolver in `SeederSmokeFixture` or seed a dev pepper). The pure-helper tests gave the real local coverage in the meantime.

Also noted: `AddVaultKeyResolver` / `AddVaultPepperResolver` are *unconditional* in `Program.cs` — there is no `Vault:Enabled` gate around them, so the `Vault:Enabled=false` the smoke fixture sets only affects health-check / secret-validator paths, not the pepper resolver. Worth knowing before anyone tries to "just disable Vault for tests".

## Follow-ups

- **MTS-3-FU1 (P3)** — `Startup.SmokeTests` env-resilience so the smoke suite runs locally, not just CI.
- **MTS-3-FU2 (P3)** — the `MenuAdminOverviewId` constant is now unused except for a doc comment (CS0414-style — matches the existing `tam an` hidden-menu constants); drop it in a future cleanup.
