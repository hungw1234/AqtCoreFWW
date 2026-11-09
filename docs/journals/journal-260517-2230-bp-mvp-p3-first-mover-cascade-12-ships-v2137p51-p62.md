# BP MVP P3 First-Mover Defect Cascade — 12 Ships v2.137p51→p62

**Date:** 2026-05-17 (19:00→22:30 ICT)
**Branch:** main
**Commits:** `17eb973d` → `1cba1ada` (12 ships)
**Session type:** Continuous debug cascade triggered by first-ever end-to-end exercise of BusinessProcess module

---

## Session goal

Bring up dev stack, navigate BP admin CRUD + Run paths, and fix every defect surfaced as the BP module hit infra conventions for the first time since its P3 ship.

## What worked (verified outcomes)

| # | Commit | Defect | Layer |
|---|--------|--------|-------|
| p51 | `17eb973d` | DbContext registered + migration shipped but `MigrateAsync` call missing from Program.cs startup loop. 2-day silent regression — outbox dispatcher spam every 5s on `Invalid object name 'businessprocess.OutboxMessages'`. | API startup |
| p52 | `c0b8581f` | Module View permission seeded but no AppMenu row → admin-business-processes invisible (DB AppMenus shadows code-fallback). | Identity seed |
| p53 | `c4191175` | Arch tests: `EveryRegisteredDbContext_Should_HaveMigrateAsyncCall_InProgramCs` + `EveryModuleWith_ViewPermissions_Should_HaveAppMenuEntry`. Surfaces SECURITY menu gap → T3-MENU-SEC. | Prevention |
| p54 | `1f2a853e` | Orval-generated URLs include `/api/v1` prefix; apiClient baseURL also `/api/v1` → `/api/v1/api/v1/...` 404. BP first end-to-end Orval consumer. | FE bridge |
| p55 | `3ce14044` | FE local DTO speculative casts (`publicId`, `code`, `latestVersion`) didn't match BE shapes (`id`, `definitionKey`, `version`). Workflow picker labels `(undefined)`. | FE typing |
| p56 | `25aa0ebd` | antd Form unmount lost field values across wizard steps. Fix: always-mount via display:none + `validateAllSteps()` validates both. | FE form |
| p57 | `bcd21ed8` | p55 DTO rename leaked into `business-process-form-maps-view.tsx` (4 stale refs). Root cause: `npx tsc --noEmit` bypassed project-scoped `tsconfig.app.json` — false-clean. Forcing-function: **always `npm run typecheck`**, never raw tsc. | FE typing |
| p58 | `a289fbdf` | CORS `WithHeaders(...)` whitelist missing `If-Match` → preflight rejects ETag-gated PUT. Server-side curl bypassed preflight → server test 204 while browser net::ERR_FAILED. **First ETag-gated endpoint FE consumes.** | CORS |
| p59 | `9578f8a3` | Workflow visualization placeholder no longer needed — P7-FU1.2 added data fetch. New `bp-workflow-flow-readonly.tsx` (~55 LOC) reuses designer NODE/EDGE/buildNodes/buildEdges. | FE feature |
| p60 | `f7566863` | antd v5 deprecation sweep: Card.bodyStyle, Tabs.destroyInactiveTabPane, static `message.X()` (94 sites). Bridge pattern via `appMessage` facade — 1 facade file, 0 hook injections per consumer. | FE cleanup |
| p61 | `efb7e20e` | BP 5 page components bypass admin design-system (raw `<div padding>+<Title>+maxWidth`). Migrate to AdminPageHeader+AdminContentCard+AdminTableToolbar. | FE style |
| p62 | `1cba1ada` | EF Core snapshot change detection doesn't catch `List.Clear()` on private backing field exposed via `_list.AsReadOnly()`. Cleared children stay Unchanged → INSERT collides with unique index. Fix: explicit ChangeTracker reconcile in repository. | BE persistence |

## What didn't / lessons (time sinks)

| L | Lesson |
|---|--------|
| 1 | **`npx tsc --noEmit` is NOT project-scoped** — it picks up the default tsconfig.json which doesn't cover src/. Always use `npm run typecheck` (script runs `tsc --noEmit -p tsconfig.app.json`). p57 ship was needed because p55 used `npx tsc` and missed 4 stale refs. |
| 2 | **Server-side curl test ≠ browser test** — bypasses CORS preflight (no Origin), no React lifecycle, no client interceptors. p58 BE returned 204 for curl while browser failed with net::ERR_FAILED on the preflight. Sequence for any FE-reported error: console → network tab status → preflight (OPTIONS) → actual request body. |
| 3 | **DDD `_list.AsReadOnly()` encapsulation + EF Core has a known interaction quirk** — child collection clears don't propagate via snapshot change detection. p62 only became visible on the SECOND replace (first save inserted into empty table; second hit unique index on existing rows). Latent from BP MVP first ship. |
| 4 | **`OnDelete(Cascade)` semantics narrow** — cascade is for parent delete, NOT collection-level orphan removal. Easy to misread as "orphan removal handled". |
| 5 | **Module-ship checklist gap** — `AddXyzInfrastructure` (DI) and `MigrateAsync` (startup) are TWO separate wirings. Forgetting one = silent multi-day regression. p53 arch test now enforces 1:1. |
| 6 | **Comment "deferred"-reason staleness** — p59 placeholder comment "no graph data in BpDefinitionDto" was true at write-time; P7-FU1.2 mooted the reason but didn't invalidate the placeholder. Codify: backlog row for every "deferred" comment. |
| 7 | **Bridge pattern > hook injection at scale** — antd `message` migration: 94 consumer sites. Adding `App.useApp()` hook to each component = 94 surface changes + utility funcs outside React tree can't use hooks. `appMessage` facade backed by NotificationBridge captures the hook instance once, all consumers stay synchronous. |
| 8 | **`count=1` in regex.sub trap** — Python script for antd-message migration hit first antd import line (no `message` token) and stopped, leaving line 20 untouched in 1 file. Fix: idempotent token rewrite + remove `count=1`. |

## Decisions made

| Decision | Rationale |
|----------|-----------|
| Orphan reconcile in `BusinessProcessRepository.UpdateAsync`, not handler | Handler stays aggregate-pure; EF Core quirk confined to persistence boundary |
| `appMessage` facade vs per-component `App.useApp()` hook | 94 sites = 94 hook injections + utility code outside React loses access; facade is mechanical find+replace + context-aware via captured hook instance |
| Read-only workflow flow as new file `bp-workflow-flow-readonly.tsx` | NOT extending `react-flow-canvas.tsx` with `readOnly` flag — that file owns the editing flow (change tracking, position commit, transition modal hook-ins); two surfaces, two files |
| Skip p62 automated test now, file backlog T3-BP-FORMMAP-REPLACE-TEST | Integration test needs DB-backed test infra in BP module; pure unit test wouldn't catch the EF Core flow |
| BP detail page uses bilateral Space+Title+Tag, not AdminPageHeader | AdminPageHeader `title: string` only — no slot for status tag + back button. Mirror workflow-definition-detail-page convention |

## Test status

| Suite | Pre-session | Post-session |
|-------|-------------|--------------|
| Architecture (ArchUnit) | 99/99 | **101/101** (p53 added 2) |
| FE typecheck (`npm run typecheck`) | unverified | Clean (verified at p57, p59, p60, p61) |
| BE build (`dotnet msbuild`) | clean | Clean (verified at p62) |
| BP integration test for ReplaceFormMaps | N/A | **TODO** — T3-BP-FORMMAP-REPLACE-TEST |

## Next session (carry-over)

1. **T3-MENU-SEC** (~1h) — Seed `[identity].AppMenus` for SECURITY module (IP_RULE, HEALTH, SIGNATURE_CONFIG) + remove SECURITY from p53 allowlist
2. **T3-BP-FORMMAP-BACKFILL** (~1h) — BE contract change: expose `FormTemplatePublicId` (Guid) on `BusinessProcessStateFormDto` + `TransitionFormDto`; drop FE broken back-fill at `business-process-form-maps-view.tsx:175-194`
3. **T3-BP-FORMMAP-REPLACE-TEST** (~30min) — Integration test for p62 fix: load BP with existing form-maps, replace with overlapping triple key, verify no unique-index violation
4. **T3-BP-FORMMAP-VIZ-INTERACTIVE** (~2h, optional) — Make read-only viewer interactive: hover-state to dim non-applicable forms in panel
5. **Run-detail page UX polish** — `<Divider>` removal in p61 may need padding adjustment; quick visual verify

## Notable patterns surfaced

- **L10 module-ship first-mover defect cascade**: BP MVP P3 → 12 defects in one evening exposed via end-to-end UI exercise. Roughly half (6) prevented by infra arch tests; half (6) needed bespoke fixes. Pattern stabilizes once next new module ships post-p53.
- **Defense-in-depth gap audit**: BP first endpoint to use `If-Match` (p58 CORS), first Orval consumer (p54), first to expose private-backing-field aggregate via Replace semantics (p62). Every "first" path tripped something.
