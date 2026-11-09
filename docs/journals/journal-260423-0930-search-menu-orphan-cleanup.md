# Session Journal — v2.128p2 Search Menu Orphan Cleanup

**Date:** 2026-04-23 09:30 (GMT+7)
**Version:** v2.128p2 (patch-level chore/infra)
**Predecessor:** v2.128p1 (SystemAdmin tenant visibility fix, commit `b3119a4f`)
**Status:** Code shipped locally — L3 seeder comment + L4 SQL script. L5 Redis flush pending user runtime execution.

---

## Root Cause

PR #45 (commit `449b18fa`, 2026-04-20) xóa hoàn toàn BE module `AqtCoreFW.Search` (4 BE projects + 3 test projects + FE page + 3 E2E specs — dead code, no callers). Commit `aa573033` dọn code orphans nhưng bỏ sót 2 entry trong `E2ETestDataSeeder.Menus.cs`:

- **Line 146:** `M(MenuSearchId, null, "search", "Tìm kiếm", "SearchOutlined", "/search", s++)` — seed menu item
- **Line 226:** `P(MenuSearchId, "SEARCH.QUERY.USE")` — permission mapping

Seeder idempotent theo fixed GUID (line 107: `if (await db.AppMenus.AnyAsync(m => m.Id == MenuDashboardId, ct)) return`) — sau khi DB đã seed lần đầu, sửa code seeder **không có hiệu lực** trên DB hiện có. Nhưng với fresh DB (dev reset, test), entry `/search` vẫn bị seed → FE fetch qua `/api/v1/menus/tree` → menu "Tìm kiếm" xuất hiện trong sidebar dù BE module đã xóa.

---

## Fix Applied — 5-Tầng Soft-Hide Pattern (v2.127p9 Precedent)

| Tầng | Layer | Hành động | Status |
|------|-------|-----------|--------|
| L1 | FE hardcoded fallback | N/A — `/search` không có trong `sidebar-menu.tsx:getMenuEntries` | Không đụng |
| L2 | FE admin-menu-entries | N/A — không phải admin menu | Không đụng |
| **L3** | BE seeder comment | Comment out line 146 + 226 `E2ETestDataSeeder.Menus.cs` | ✅ DONE |
| **L4** | DB runtime soft-hide | SQL script `scripts/260423-soft-hide-search.sql` — `UPDATE IsActive=0` idempotent | ✅ DONE (file created, execution pending user) |
| **L5** | Redis cache flush | `redis-cli DEL menu-tree:*` | ⏳ PENDING user |

**Philosophy "Option B" (non-destructive):** Không DELETE record. Admin `/admin/menus` vẫn thấy row "Kích hoạt: Không" cho audit trail. Permission `SEARCH.QUERY.USE` giữ nguyên ở `PermCodes.cs:468` + `seed-permissions.sql:115` — cleanup batch sau nếu quyết định không restore Search module.

---

## Files Changed

### 1. `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` (L3)

**Line 146** (menu entry):
```csharp
// M(MenuSearchId,      null,              "search",       "Tìm kiếm",           "SearchOutlined",          "/search",             s++), // tạm ẩn — orphan sau PR #45 (BE AqtCoreFW.Search module đã xóa). Bật lại: khôi phục BE module + uncomment + reseed DB
```

**Line 226** (permission mapping):
```csharp
// P(MenuSearchId,     "SEARCH.QUERY.USE"), // tạm ẩn cùng menu search
```

Static Guid declaration `MenuSearchId` tại line 29 giữ nguyên — pattern đồng bộ với `MenuConsentId` (line 33) và `MenuRtbfId` (line 85), dễ rollback.

### 2. `scripts/260423-soft-hide-search.sql` (NEW — L4)

Clone pattern `scripts/260422-soft-hide-consent-rtbf.sql`. Idempotent `UPDATE [identity].[AppMenus] SET [IsActive] = 0 WHERE [Code] = 'search' AND [IsActive] = 1` với `SET QUOTED_IDENTIFIER ON` (fix Msg 1934 cho filtered indexes). Include inline rollback note + required Redis flush command.

---

## Lessons Learned

### 1. Seeder Idempotent = Song Song Code + DB
Seeder skip-by-fixed-GUID design: comment code alone **không đủ** cho DB đã seed. Pattern v2.127p8 (code) + v2.127p9 (DB+Redis) đã chứng minh cần **full 5 tầng** mới isolate xong orphan. v2.128p2 apply nguyên pattern này.

### 2. Cleanup Orphan Cần Audit BOTH Code + Seed Data
PR #45 rà code dead module rất kỹ nhưng bỏ sót seeder entry vì grep theo tên namespace (`AqtCoreFW.Search.*`) — không match dòng seeder chỉ chứa string literal `"search"` + `"/search"` + `"SEARCH.QUERY.USE"`. Lesson: khi xóa module, phải grep thêm theo **route + permission code + menu code literal** qua seeder files.

### 3. Version Number Coordination
v2.128p1 đã dùng cho SystemAdmin tenant visibility fix (commit `b3119a4f`) — search orphan cleanup shipped cùng ngày nhưng **tách version** thành v2.128p2 để changelog/audit trail phân biệt được 2 fix độc lập. Luôn verify git log trước khi gán version.

### 4. docs-manager Drift Risk
Lần delegate docs-manager đầu tiên merge nhầm search orphan vào v2.128p1 entry + hallucinate code block trong journal. Trust-but-verify protocol (grep sample + version string check) phát hiện drift → reconcile pass 2. Pattern: prompt docs-manager phải include **version number explicitly** + **verification evidence** (grep line N shows v2.X.Y).

---

## Verification Protocol

### Pre-runtime (code only)
```bash
# L3 verified — 2 comment lines in seeder
grep -n "MenuSearchId" src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs
# Line 29: declaration (kept)
# Line 146: // M(...) — commented
# Line 226: // P(...) — commented

# L4 verified — SQL file exists
ls scripts/260423-soft-hide-search.sql
```

### Runtime (pending user)
```bash
# Execute L4 script
sqlcmd -S localhost,1433 -U sa -P <pass> -d AqtCoreFW -i scripts/260423-soft-hide-search.sql
# Expected: "Soft-hidden 1 menu(s). Now flush Redis: redis-cli DEL menu-tree:*"

# Flush L5 cache
docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' | xargs -I {} docker exec docker-redis-1 redis-cli DEL {}

# Verify menu gone — browser F5 http://localhost:5173 → "Tìm kiếm" biến mất khỏi sidebar
```

---

## Next Steps

1. User chạy runtime L4 + L5 (sqlcmd + redis-cli)
2. Commit 2 code files + docs sync qua git-manager
3. Optional cleanup batch: nếu quyết định không restore Search module, xóa `SEARCH.QUERY.USE` khỏi `PermCodes.cs` + `seed-permissions.sql` (backlog ưu tiên thấp)
4. Monitor: nếu chưa ai báo miss menu sau 1 tuần → coi orphan cleanup là thành công

---

## References

- **Prior soft-hide precedent:** `docs/journals/journal-260422-1845-v2.127p9-runtime-soft-hide-consent-rtbf.md`
- **Search module removal:** PR #45 commit `449b18fa` (2026-04-20)
- **Orphan code cleanup (prior):** commit `aa573033`
- **v2.128p1 (predecessor):** commit `b3119a4f` (SystemAdmin tenant visibility)
- **v2.128 (baseline):** commit `e00b4be7` (BTC org seeding 1139 OrgUnits)
