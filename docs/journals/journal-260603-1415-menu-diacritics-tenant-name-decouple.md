# Journal — Menu diacritics, tenant-name selector, Identity→Org decouple

**Date:** 2026-06-03
**Commits:** `47e27b77` (menu diacritics + self-heal), `efea44c5` (tenant selector uses Name), `f6c4a615` (decouple ListTenants from OrgUnit.Contracts)
**Trigger:** user report — UI tiếng Việt mất dấu + nghi seed trùng "2 Bộ Nội Vụ"

## What & why

Three linked fixes from one bug report, diagnosed via `/ck-debug` then implemented via `/cook`.

### 1. Menu labels mất dấu (`47e27b77`)
Root cause: sidebar menu là **DB-seeded** từ `E2ETestDataSeeder.Menus.cs`, các title literal viết ASCII thẳng trong source ("He thong", "Cong viec nen"...). Không phải lỗi encoding/font — data tenant/org trong DB đều có dấu (verify hex UTF-16). Header subtitle riêng: `web/.env.local` ghi đè `VITE_APP_SUBTITLE` không dấu (code default + `.env.example` đã đúng).

Fix: viết lại ~40 title/description sang có dấu. Mấu chốt: menu seeder **insert-only-by-PublicId, không UPDATE** → sửa source không tự áp. Thêm **update-on-diff** vào `SeedMenusAsync` (so Title/Description/Icon/Route/SortOrder, `AppMenu.Update()` khi khác, flush Redis menu-tree) → self-heal mỗi lần boot. Live: `updated 54 label(s)` + flush. Structural fields (ParentId/Code) ngoài scope reconcile.

### 2. "2 Bộ Nội Vụ" + tenant selector (`efea44c5`)
Không phải seed trùng. 3 tenant thật: BTC (b7c1, 10k), BNV (b7c2, 1498), dev tenant `…0001` ("Tenant 00000000", 6 user). Dropdown override hiển thị **tên org-root** thay vì `tenant.Name` (`ListTenantsQueryHandler` cũ) → dev tenant org-root "Bộ Nội Vụ" đụng tên BNV thật.

Root cause = nợ lịch sử: selector ra đời (commit `f2a4eb91`, 04/04) khi tenant chưa có Name (chỉ GUID) → lấy org-root làm nhãn. Sau khi tenant có Name (v2.133p5) override không gỡ → sống lâu hơn lý do. Fix: tách `ResolveTenantDisplayName`, đảo precedence `tenant.Name` thắng, org-root fallback. +6 unit test.

### 3. Bỏ enrichment + cắt coupling (`f6c4a615`)
Sau khi `tenant.Name` thắng, org-root fallback thành code chết. Gỡ hẳn `IOrgUnitModuleClient` khỏi `ListTenantsQueryHandler` + gỡ `ProjectReference OrgUnit.Contracts` khỏi `Identity.Application.csproj` (handler là consumer DUY NHẤT của contract đó trong Application). Cắt coupling Identity.Application→OrgUnit.Contracts. `ResolveTenantDisplayName` + test (vừa thêm ở #2) bị xóa. `DataScopeService` (Identity.Infrastructure) vẫn dùng client → coupling đó out-of-scope, giữ.

## Verification
- Build slnx 0E/0W (×2) · `Identity.Application.Tests` 245/245 · `MenuSeederPurgeLogicTests` 10/10
- Live: menu + header đủ dấu; dropdown 3 tenant phân biệt; chip dev tenant = "Tenant 00000000"
- code-reviewer ×2: APPROVE 0 Critical/High/Medium

## Lessons
- Phân biệt **data-trong-DB** vs **UI chrome**: cùng trang, tenant name có dấu nhưng menu mất dấu → loại ngay encoding/font, soi nguồn seed.
- Insert-only idempotent seeder = bẫy "sửa source không áp" → cần update-on-diff để self-heal.
- Override "sống lâu hơn lý do" là code smell: selector dùng org-root chỉ vì tenant từng không có Name; fix gốc là đảo precedence, không phải xóa dev tenant (vá triệu chứng).
- Cắt coupling thật = gỡ ProjectReference, không chỉ gỡ usage; build 0E/0W chứng minh không còn dangling.

## Follow-up (commit `d2c2d448`, /cook)
- ✅ **DONE**: gỡ `IOrgUnitModuleClient.ListTenantRootsAsync` khỏi contract + `InProcessOrgUnitModuleClient` impl (caller-less repo-wide sau `f6c4a615`). `OrgUnitSummary` DTO giữ (còn dùng bởi GetRootOrgUnit* methods). Sole implementer, 0 consumer → build 0E/0W·`Organization.Tests` 76/3-skip/0·review APPROVE 0 Crit/High/Med. Docs (dependency-map, system-architecture) updated.

## Open / follow-ups
- `web/.env.local` fix là local (gitignored) — không áp máy khác trừ khi máy đó cũng có `.env.local` lệch.
- Chưa push; chưa bump version/changelog (refactor session).
