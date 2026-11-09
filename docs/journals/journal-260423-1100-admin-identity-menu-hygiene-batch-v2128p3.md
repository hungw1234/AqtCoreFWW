---
date: 2026-04-23
time: 11:00
version: v2.128p3
topic: Admin Identity Menu Hygiene Batch (soft-hide 3 menus)
related:
  - v2.128p2 (search orphan, same session)
  - v2.127p9 (consent + rtbf soft-hide, precedent)
---

# Admin Identity Menu Hygiene Batch — v2.128p3

## Bối cảnh

Session-2 follow-up sau v2.128p2 (search orphan cleanup). Anh yêu cầu ẩn tiếp 3 admin identity menus chưa dùng hoặc chưa hoàn thiện UX:

1. `/admin/sod-rules` — Quy tắc SoD (Separation of Duties)
2. `/admin/policy-rules` — Chính sách quyền
3. `/admin/access-reviews` — Rà soát quyền

**Lý do ẩn:** Các feature này **vẫn hoạt động đầy đủ** ở backend (module Identity, T4-06 SoD refactor v2.127 hoàn tất), nhưng sidebar menu chưa có business case cụ thể để publish cho user. Ẩn khỏi sidebar giảm clutter, giữ URL route + BE endpoints + permissions để admin vẫn access được khi cần.

## Chiến lược — 5-tầng pattern (kế thừa v2.127p9)

| Tầng | Thay đổi | Mục đích |
|------|----------|----------|
| **L1** FE sidebar hardcoded | N/A | 3 menus không có ở `sidebar-menu.tsx` gốc, chỉ ở admin sub-menu. |
| **L2** FE admin-menu-entries | `web/src/layouts/admin-menu-entries.ts` — 3 MenuItem entries + 3 icon imports commented. | Không render trong Admin → Identity & Access group. |
| **L3** BE seeder | `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` — 3 M entries + 3 P permissions commented. | Fresh DB install không seed menu rows. |
| **L4** DB runtime | 2 SQL scripts idempotent `UPDATE IsActive=0`. | Existing DB rows hidden từ `/api/v1/menus/tree` query. |
| **L5** Redis flush | `redis-cli --scan --pattern 'menu-tree:*' \| xargs DEL`. | Invalidate fast-path cache, trigger rebuild từ DB. |

**Philosophy B (non-destructive):** GUID constants + permission codes giữ lại. Admin `/admin/menus` vẫn thấy rows với `IsActive=0` cho audit trail. Rollback 1 lệnh.

## Thực thi

### L2 FE edits (`admin-menu-entries.ts`)

```typescript
// Line 5 (AuditOutlined icon import)
// AuditOutlined, // tạm ẩn cùng menu Access Reviews — uncomment khi bật lại /admin/access-reviews

// Line 24 (BlockOutlined icon import)
// BlockOutlined, // tạm ẩn cùng menu SoD Rules — uncomment khi bật lại /admin/sod-rules

// Line 25 (StopOutlined icon import)
// StopOutlined, // tạm ẩn cùng menu Policy Rules — uncomment khi bật lại /admin/policy-rules

// Line 133 (SoD Rules menu item)
// { key: '/admin/sod-rules', label: t('nav.sodRules'), icon: React.createElement(BlockOutlined) }, // tạm ẩn — route vẫn sống

// Line 134 (Policy Rules menu item)
// { key: '/admin/policy-rules', label: t('nav.policyRules'), icon: React.createElement(StopOutlined) }, // tạm ẩn — route vẫn sống

// Line 136 (Access Reviews menu item)
// { key: '/admin/access-reviews', label: t('nav.accessReviews'), icon: React.createElement(AuditOutlined) }, // tạm ẩn — route vẫn sống
```

**Lưu ý:** `IDENTITY_PATHS` array (lines 57-71) **giữ nguyên** — pattern theo v2.127p4 RTBF. Route registry cần các paths này để `getAdminCategoryKey(pathname)` resolve breadcrumbs đúng khi admin vào URL trực tiếp.

### L3 BE seeder edits (`E2ETestDataSeeder.Menus.cs`)

```csharp
// Lines 166-169 (M entries)
// M(MenuSodId, MenuGrpIdentityId, "admin-sod", ...),             // tạm ẩn
// M(MenuPolicyRulesId, MenuGrpIdentityId, "admin-policy", ...),  // tạm ẩn
// M(MenuAccessRevId, MenuGrpIdentityId, "admin-access-rev", ...), // tạm ẩn

// Lines 241-244 (P permission mappings)
// P(MenuSodId, "IDENTITY.SOD.VIEW"),
// P(MenuPolicyRulesId, "IDENTITY.POLICY.VIEW"),
// P(MenuAccessRevId, "IDENTITY.ROLE.VIEW"),
```

### L4 SQL runtime (2 scripts)

**`scripts/260423-soft-hide-sod-rules.sql`** — single menu:
```sql
UPDATE [identity].[AppMenus] SET [IsActive]=0
 WHERE [Code]='admin-sod' AND [IsActive]=1;
-- Result: 1 row affected
```

**`scripts/260423-soft-hide-policy-and-access-reviews.sql`** — batch:
```sql
UPDATE [identity].[AppMenus] SET [IsActive]=0
 WHERE [Code] IN ('admin-policy', 'admin-access-rev') AND [IsActive]=1;
-- Result: 2 rows affected
```

**Idempotent guard `AND IsActive=1`:** re-run lần 2 sẽ 0 rows affected (không double-update, không lỗi).

### L5 Redis flush

```bash
docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' \
  | xargs -I {} docker exec docker-redis-1 redis-cli DEL {}
# Result: 1 (default tenant cache key deleted)
```

### Verify

```sql
SELECT [Code], [Title], [IsActive] FROM [identity].[AppMenus]
 WHERE [Code] IN ('admin-policy','admin-access-rev','admin-sod','search')
 ORDER BY [Code];
```

Output: 4 rows, all `IsActive=0` (cumulative với v2.128p2 search orphan).

## Files

- `web/src/layouts/admin-menu-entries.ts` — 3 menu entries + 3 icon imports commented
- `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` — 3 M + 3 P commented
- `scripts/260423-soft-hide-sod-rules.sql` (NEW, 40 lines idempotent)
- `scripts/260423-soft-hide-policy-and-access-reviews.sql` (NEW, 40 lines idempotent batch)

**Breaking Changes:** None.
**Build risk:** Zero (TS typecheck preserved — icon imports commented cùng lúc với tham chiếu → không unused-import warning).
**Rollback:** `UPDATE IsActive=1 WHERE Code IN ('admin-sod','admin-policy','admin-access-rev')` + Redis flush + uncomment seeder/FE lines.

## Lessons

1. **Pattern phù hợp khi feature chưa có business case:** Xóa DELETE sẽ mất audit; comment-only sẽ drift (DB row cũ vẫn show). 5-tầng pattern cân bằng: route sống để anh test được, menu ẩn để không clutter UI user, DB row lưu audit trail qua admin `/admin/menus`.
2. **Batch grouping chính xác hơn đơn lẻ:** 3 menus đều có cùng nghiệp vụ (admin identity menu hygiene), cùng tầng, cùng pattern. Gộp thành p3 chứ không split p3a/p3b/p3c — giảm noise trong changelog.
3. **`IDENTITY_PATHS` array không động:** Routes cần giữ để breadcrumb + `getAdminCategoryKey()` hoạt động khi admin access URL trực tiếp. Đã học từ v2.127p4 RTBF hide — không phải lần đầu.
4. **Idempotent `AND IsActive=1` guard quan trọng:** Không có guard này, re-run script trên DB đã flip sẽ lưu history change thừa (nếu có audit trigger) và log PRINT misleading.

## Next

- User test manual F5 `http://localhost:5173` — xác nhận 3 menus mất khỏi Admin → Identity & Access sidebar.
- User test URL routes `/admin/sod-rules`, `/admin/policy-rules`, `/admin/access-reviews` vẫn truy cập được.
- Quyết định commit strategy: gộp p2+p3 thành 1 commit hay tách 2/3 commits.
- (Optional) Batch thêm menus admin identity khác nếu cần (`/admin/credential-policies`, `/admin/delegations`, `/admin/sessions`, `/admin/abac-rules`, `/admin/data-scopes`).
