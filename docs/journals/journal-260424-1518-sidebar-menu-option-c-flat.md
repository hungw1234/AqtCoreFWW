# Journal — Sidebar Menu Flat Restructure (Option C) + Test Mock Debt Fix

**Date:** 2026-04-24 15:18 ICT | **Branch:** `main` | **Commits:** `123801a5`, `4dd6fc3d` (+ this journal)

## Context — 3 cook cycles, 1 session

| # | Cycle | Scope |
|---|-------|-------|
| 1 | `/ck-debug` "Bỏ chức 'new' ở menu Copilot" | Remove "New" badge from Copilot menu entry (BADGE_MAP + fallback) |
| 2 | `/cook` "Cấu trúc sắp xếp lại menu sidebar" | Flatten Admin wrapper to Option C (10 top-level slots) |
| 3 | `/cook` "Fix trước, xanh rồi commit" | Fix 2 pre-existing test failures (NotificationBell + Topbar) before landing |

## Decision — Option C: Flat top-level

Anh chọn Option C từ 3 phương án AskUserQuestion preview. Structure:

```
(group) NGHIỆP VỤ              — Dashboard, Cases, Inbox, Forms, Files, Signatures
(group) AI & TRỢ LÝ            — AI Search, Copilot, Chat
(group) TÍCH HỢP & BÁO CÁO     — Integration ▸, Audit, Reporting ▸
(submenu) Định danh & Quyền ▸  — 10 items
(submenu) Nội dung & Quy trình ▸ — 5 items
(submenu) Hệ thống ▸           — 6 items
(submenu) API & Webhooks ▸     — 2 items
(submenu) AI & Automation ▸    — 1 item
(submenu) Đối tác tích hợp ▸   — 1 item
(leaf) Tổng quan admin         — /admin
```

Rationale: 6 admin domains có depth riêng, không bị chôn dưới "Quản trị" submenu dài ngoằng. Anh nhận xét "trong Quản trị nhiều quá, tách bớt ra".

## Key implementation facts

1. **2 data paths** — BE seeds `identity.AppMenus` (source of truth runtime); FE `sidebar-menu.tsx` fallback chỉ active khi API menu-tree fail/empty. Phải update cả 2 để không drift.
2. **Sentinel GUID thay đổi** — `MenuDashboardId` → `MenuGrpBusinessId` (0x90). Dev DB cũ phải purge trước khi reseed:
   ```sql
   DELETE FROM identity.MenuRolePermissions;
   DELETE FROM identity.AppMenus;
   ```
   Restart API → seeder detect sentinel missing → insert 46 menu records + auto-flush Redis `menu-tree:*` cache.
3. **Type widening** — `MenuItemGroup.children` mở rộng từ `MenuItem[]` → `MenuEntry[]` để group "Tích hợp & Báo cáo" chứa nested submenus (Integration ▸, Reporting ▸). Backward compat vì admin groups cũ vẫn thỏa.
4. **FE buildItemsFromApi không đổi** — BE seeds group header với `route=null, icon=null` → FE tự render `type:'group'` theo rule hiện có `children.length > 0 && !route && !icon`.

## Test mock debt (side-fix)

Pre-existing failures hiển diện khi chạy full FE suite:
- `notification-bell.test.tsx` (7 fails) — mock `@/features/notifications/notification-api` thiếu export `useMarkAllAsRead` (hook thêm sau khi test viết, mock quên sync).
- `topbar.test.tsx` (4 fails) — Topbar render `NotificationBell` gọi `useQueryClient` nhưng test không wrap `QueryClientProvider`.

Fix cả 2 trước commit → **789/789 tests pass** (từ 778/789 trước đó).

## Rollback

`git revert 4dd6fc3d 123801a5` — 2 commit atomic, không schema migration, reseed data scripted trong comment. BE seed idempotency protects against partial state.

## Finalize status

| # | Artifact | Status | Evidence |
|---|----------|--------|----------|
| 1 | docs/dependency-map.md | N/A | Không entity/endpoint/cross-module change |
| 2 | docs/project-roadmap.md | N/A | Không phase transition + không version bump |
| 3 | docs/project-changelog.md | N/A | UI polish — gom vào next patch bundle |
| 4 | backlogs/backlog.md | N/A | Không tier change |
| 5 | memory/project-state.md | N/A | Cùng baseline v2.131p2 session trước |
| 6 | memory/MEMORY.md | N/A | Không memory mới |
| 7 | Session journal | ✅ | This file |

## Follow-ups

- Dev: purge + restart API để thấy BE menu structure mới (FE fallback không cần reseed — tự apply).
- Next patch bundle (v2.131p3 hoặc v2.132): gom changelog entry cho UI polish batch.

## Unresolved

- [ ] `/integration/partners` (gateway view) và `/admin/integration/partners` (admin CRUD) hiện share Partners data — cần xác nhận khác biệt business ý định hay có thể merge route.
- [ ] `MenuGrpAiAutomationId` và `MenuGrpPartnerIntegrationId` submenu mỗi cái chỉ 1 item — có thể flatten thành leaf link nếu không có plan thêm item. Tạm giữ theo Option C preview đã approved.
