# Journal — Sidebar "Thông báo" Menu Removal Verification

**Date:** 2026-04-15 19:00 (Asia/Saigon)
**Session type:** Debug / verification
**Scope:** Frontend sidebar menu + Identity module menu pipeline
**Outcome:** Verified — no code changes needed; menu removal already effective.

---

## Context

User asked "sao vẫn còn menu Thông báo ???" after editing `sidebar-menu.tsx` and `admin-menu-entries.ts` to remove a top-level "Thông báo" entry. HMR logs showed multiple edits to those files between 4:46-4:54 PM, followed by Vite dev process termination.

## Investigation trace

1. **FE fallback inspection** — Read `web/src/layouts/sidebar-menu.tsx` + `admin-menu-entries.ts`. Neither contained a top-level "notifications"/"Thông báo" entry. Found `BADGE_MAP: { notifications: 3 }` — badge only, doesn't create a menu node.
2. **Render precedence** — Confirmed `sidebar-menu.tsx:199-201` prefers API-driven `menuTree` when non-empty; hardcoded fallback only runs when API returns empty tree.
3. **BE seed scan** — `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` defines only `admin-notif-tpl` ("Mẫu thông báo") under Admin › Content group. **No top-level "Thông báo" in seed.**
4. **Idempotency trap identified** — Seeder skips entirely when `MenuDashboardId` exists (`SeedMenusAsync` line 93). Code edits to the seed **never propagate** to an existing DB.
5. **Cache layer** — `MenuService` caches `menu-tree:{tenantId}` keys in Redis, TTL 30 min. Seeder flushes only when re-seed actually runs.
6. **Live DB query** (sqlcmd against `localhost,1433`) — `[identity].[AppMenus]` contains exactly one row matching `%thông báo%`: `admin-notif-tpl` (legitimate). **No stale top-level row exists.**
7. **User confirmed** — menu already removed correctly after fresh render/rebuild.

## Options proposed & withdrawn

Initially offered three remediation options (SQL DELETE, migration, drop+reseed) assuming stale DB data. After DB verification, **all options withdrawn** — the SQL DELETE would have wrongly removed the legitimate `admin-notif-tpl` template menu.

## Side findings (not remediated)

- `[identity].[AppMenus]` contains **dozens of top-level `TC076-MENU-*` rows** — test-case 076 artifacts never cleaned. Candidates for a cleanup migration or test teardown fix.
- `ECONNREFUSED /hubs/notifications/negotiate` in Vite proxy log — BE Api.Host not running. Non-fatal.

## Lessons

1. **Verify DB state before proposing DELETE.** The first response prescribed SQL cleanup based on a hypothesis ("stale seed row"). Direct query invalidated the hypothesis and saved a wrong delete. Always query before prescribing mutations.
2. **Idempotent-seed-by-fixed-GUID is a trap.** Developers editing the seed code assume changes will apply on next run — but `AnyAsync(MenuDashboardId)` short-circuits the whole pass. Worth documenting in the seeder or switching to row-level upsert+soft-delete reconcile (non-trivial, deferred).
3. **Ambiguous Vietnamese terms.** "Thông báo" matches both the bell icon tooltip and "Mẫu thông báo" templates. Distinguishing in the clarify question prevented misdirected action.

## Artifacts

- **Changed:** `docs/project-changelog.md` — added DEBUG entry (2026-04-15).
- **Created:** this journal.
- **No code changes.**

## Unresolved

- `TC076-MENU-*` test-artifact rows in prod DB — should be cleaned (deferred).
- Seeder idempotency design — consider reconcile-mode upsert in a future task (non-urgent; low risk as-is since admin UI can CRUD menus directly).
