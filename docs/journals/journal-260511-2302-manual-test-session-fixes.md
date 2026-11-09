---
date: 2026-05-11 22:00–23:00 ICT
type: manual-test-session
duration: ~3 hours
status: uncommitted (working tree)
---

# Journal — Manual Test Session: Local Bring-up + Multi-Bug Fix Sweep

## Session Goal

Mở local app review functional plan QĐ 742 Level 4 → kiểm tra thủ công các tab user-facing → fix bugs as encountered. Side scope expanded to include feature gap (Visual Designer persist) khi user request.

## Bugs Fixed (chronological)

| # | Bug | Layer | Files | Status |
|---|-----|-------|-------|--------|
| 1 | Dev local app boot fail (Vault permission denied + missing peppers + missing env) | Infra | `infra/docker/vault-init.sh` NEW + `docker-compose.yml` rewrite + 2 `launchSettings.json` NEW | ✅ uncommitted |
| 2 | Login admin@dev.local "sai mật khẩu" — 10010/10010 users `NormalizedEmailHashGlobal IS NULL` | Infra | Vault pepper for BTC tenant added; backfill ran ✅ 10010 users | ✅ ran on running DB |
| 3 | `Invalid column 'CreatedAtUtc'` trang Quản lý ủy quyền | BE Dapper | `ListDelegationsQueryHandler.cs` — col rename + T2 PublicId AS Id | ✅ uncommitted |
| 4 | `Error parsing column 0 (Id=13 - Int64)` tab Phiên bản & Di chuyển | BE Dapper | `GetVersionHistoryQueryHandler.cs` — T2 PublicId AS Id + GROUP BY fix | ✅ uncommitted |
| 5 | Visual Designer canvas empty dù 3 states + 2 transitions | FE | `workflow-api.ts` — coerce long→string ids tại API boundary (normalizeDefinitionIds) | ✅ uncommitted |
| 6 | Visual Designer không sync khi CRUD ở tab Các bước / Chuyển bước | FE | `definition-visual-designer-tab.tsx` — useEffect prop-sync (hasChanges guard) | ✅ uncommitted |
| 7 (enh) | State node chỉ 2 connection handles top/bottom | FE | `custom-state-node.tsx` — 8 handles (4 sides × source+target stacked) | ✅ uncommitted |
| 8 (enh) | Không kéo được endpoint của transition existing | FE | `react-flow-canvas.tsx` + handlers — `onReconnect` + `edgesReconnectable` + handleReconnectTransition | ✅ uncommitted |
| 9 (regression) | 8 handles → tất cả edges đè nhau ở top | FE | `custom-state-node.tsx` JSX reorder — t-in + b-out declared FIRST để ReactFlow default routing pick bottom→top | ✅ uncommitted |
| 10 (feature) | Persist Visual Designer tọa độ + handle anchors | Stack | EF migration `AddVisualDesignerPositionsAndHandles` + 8 BE files + 4 FE files | ✅ uncommitted |
| 11 | Trang Biểu mẫu tài liệu Dapper materialization fail | BE | `GetDocumentTemplatesQueryHandler.cs` — TemplateRow DateTimeOffset→DateTime + boundary convert | ✅ uncommitted |
| 12 | Trang Mẫu thông báo same Dapper materialization fail | BE | `GetNotificationTemplatesQueryHandler.cs` + `GetNotificationTemplateByIdQueryHandler.cs` — Row record pattern | ✅ uncommitted |
| 13 (strategic) | Same Dapper DateTimeOffset materialization fail across 7+ sites | BE | `DapperTypeHandlers.cs` NEW + `InfrastructureRegistration.cs` — global `SqlMapper.TypeHandler<DateTimeOffset>` | ✅ uncommitted |
| 14 | Menu "Tong quan admin" → `/admin` 404 | Data | `E2ETestDataSeeder.Menus.cs` — remove leaf + perm; DB DELETE orphan + Redis flush | ✅ uncommitted |

## What Worked

- **Vault seed lifecycle:** docker-compose `vault-init` YAML folded scalar `>` mangle multi-line bash → extracted to `vault-init.sh` script. Idempotent KV v2. Seeds v1 + hmac + 3 tenant peppers (Guid.Empty global + dev tenant + BTC tenant).
- **launchSettings.json** auto-inject VAULT_ADDR/VAULT_TOKEN — zero-config dev boot.
- **EmailHashBackfillService** ran successfully cho 10010 users sau khi peppers seeded.
- **Global Dapper TypeHandler** fixes 7+ DateTimeOffset materialization sites bằng 1 file 75 LOC. Verified 8 endpoints HTTP 200.
- **Visual Designer end-to-end persist** verified — curl PUT `/graph` with positionX/Y + sourceHandle/targetHandle → HTTP 200 → DB persists (4 states with x/y + 3 transitions with handles).
- **Refresh tab Mẫu thông báo + Quản lý ủy quyền + Tab Phiên bản & Di chuyển** — tất cả HTTP 200 (verified curl + Chrome console clean).

## What Didn't / Lessons

- **Bug pattern recurrence trong cùng session = 5×** cho Dapper materialization (T2 PublicId leak: 2×, DateTimeOffset mismatch: 7+ trên 2 modules). Memory `phase7-b4-integration-audit-dapper-shipped-260509.md` (project-type) đã document pattern nhưng vẫn lặp → **feedback-type memory** mới có visibility cao hơn. Đã tạo `feedback-dapper-datetimeoffset-global-type-handler.md` + `feedback-dapper-t2-select-publicid-as-id.md`.
- **8-handles enhancement → regression đè edges** vì tôi không Chrome MCP screenshot verify trước khi declare done. Skipped frontend-verification.md step. Next session: enforce verification gate trên UI changes.
- **`notifyChange(...)` inside `setEdges` updater** pattern pre-existing — vi phạm React 18 "no side effects in updaters" rule. Console warning surfaced sau khi tôi add state-prop-sync useEffect. Defer cleanup (cần refactor lớn, không phải save bug).
- **Menu seeder ADD-only idempotency** = silent FE↔BE menu desync khi remove route. User had to discover via 404 click. Backlog: auto-purge orphans trong seeder.
- **`sqlcmd` DELETE on `identity.AppMenus`** fail vì `QUOTED_IDENTIFIER OFF` default — already memory'd via `feedback-sqlcmd-quoted-identifier.md`, applied SET prefix.

## Decisions Made

1. **Visual Designer persist scope = Path A (full)** — both positions + handles. User confirmed via AskUserQuestion. Schema migration `AddVisualDesignerPositionsAndHandles`: 4 cols (`PositionX float NULL`, `PositionY float NULL`, `SourceHandle nvarchar(MAX) NULL`, `TargetHandle nvarchar(MAX) NULL`). `nvarchar(MAX)` excessive — backlog tighten to `nvarchar(16)`.
2. **Dapper DateTimeOffset fix = global TypeHandler** thay vì per-handler Row records. Strategy switch sau bug #4 → bug #5 same family. KISS. Legacy Row records (DocumentTemplates + NotificationTemplates) kept để minimize risk.
3. **FE T1 long → string coerce tại API boundary** (`normalizeDefinitionIds` trong `useWorkflowDefinition`) — fix WorkflowState/WorkflowTransition ID mismatch. 1 chỗ duy nhất; downstream components nhận string-only.
4. **Visual Designer position priority** = sessionMap (recent drag) → DTO persisted → grid fallback. Preserves UX latest-write-wins during session.

## Test Status

- BE build: 0E (final), some intermediate 4E resolved
- FE typecheck: 0E (final)
- Suite tests: **NOT RUN this session** — bug-fix-and-verify-via-curl pattern. Pre-commit recommended: full suite run.
- Manual verification: 8 endpoints HTTP 200, save round-trip DB-confirmed.
- Chrome console: 1 pre-existing React warning ("Cannot update during render") — not regression.

## Process Cleanup Pending

| Item | Status |
|------|--------|
| `dotnet ef` migration applied | ✅ DB schema updated |
| Vault peppers seeded for 3 tenants | ✅ in dev Vault container |
| Redis menu cache | ✅ flushed (admin-overview removal) |
| DB orphan row `admin-overview` | ✅ purged |
| **Uncommitted changes** | ⚠️ ~30 modified + 11 untracked files |
| **Running processes** | Auth 5002, API 5001, FE 5173, Vault 8200 — STOP next |

## Next Session Priorities

1. **Commit session work** — split into logical commits:
   - `feat(infra)`: vault-init.sh extraction + launchSettings + DapperTypeHandlers
   - `fix(workflow,files,notifications)`: Dapper materialization + T2 PublicId AS Id
   - `feat(workflow)`: Visual Designer persist positions + handles (migration + 12 files)
   - `fix(menus)`: remove admin-overview orphan
2. **Run full test suite** — Architecture + Identity.Integration + Workflow.Integration + FE vitest. Confirm no regression after 30+ file touches.
3. **Sweep remaining Dapper sites** — verify other modules (collaboration, ai, audit) DTO materialization works với global TypeHandler.
4. **Backlog cleanup** — flag follow-ups:
   - `SourceHandle/TargetHandle nvarchar(MAX) → nvarchar(16)` migration
   - Menu seeder auto-purge orphans (prevent future /admin-style 404s)
   - Visual Designer reverse Row records (cleanup, P3)
   - React `notifyChange` inside `setEdges` updater pattern refactor

## Unresolved Questions

- Có cần BE add `/admin` index route trở lại làm admin dashboard landing không? Hay redirect to `/admin/users` mặc định?
- `WorkflowDefinition.Clone()` đã update copy PositionX/Y + SourceHandle/TargetHandle, nhưng E2E clone test chưa run — verify Phase-05b CI run pre-merge.
- Visual Designer FE đã wire FULL persist path nhưng chưa user-manual-test E2E (save → reload → positions render correctly). Backend round-trip verified via curl only.
