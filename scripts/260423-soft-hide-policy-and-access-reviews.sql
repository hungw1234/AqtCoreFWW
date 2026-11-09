-- 260423-soft-hide-policy-and-access-reviews.sql
-- Version: v2.128p3 (2026-04-23) — batch admin identity menu soft-hide
-- Purpose: Soft-hide 2 admin identity menus khỏi sidebar,
--          đồng bộ với hide intent FE admin-menu-entries.ts + BE seeder comments.
--
-- Background:
--   Batch follow-up của 260423-soft-hide-sod-rules.sql (admin-sod).
--   Các feature vẫn hoạt động đầy đủ — chỉ ẩn MENU khỏi sidebar.
--   Route + BE endpoints + permissions giữ nguyên, admin có thể truy cập qua URL trực tiếp.
--
-- Strategy (Option B — soft-hide, giống v2.127p9 pattern):
--   UPDATE IsActive=0 thay vì DELETE. Admin /admin/menus vẫn thấy row
--   với cột "Kích hoạt: Không". Bật lại = UPDATE IsActive=1.
--
-- Affected rows (idempotent — re-run an toàn):
--   - admin-policy       (Id 60000000-0000-0000-0000-000000000056, perm IDENTITY.POLICY.VIEW, route /admin/policy-rules)
--   - admin-access-rev   (Id 60000000-0000-0000-0000-000000000058, perm IDENTITY.ROLE.VIEW,   route /admin/access-reviews)
--
-- Required cleanup AFTER this script:
--   docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' | xargs -I {} docker exec docker-redis-1 redis-cli DEL {}
--
-- Rollback (bật lại):
--   UPDATE [identity].[AppMenus] SET [IsActive] = 1 WHERE [Code] IN ('admin-policy','admin-access-rev');
--   + flush Redis menu-tree:* như trên.

USE [AqtCoreFW];
GO

-- SET QUOTED_IDENTIFIER ON — required by indexed views / computed columns / filtered indexes
-- on AppMenus (SQL Server raises Msg 1934 otherwise).
SET QUOTED_IDENTIFIER ON;
GO

UPDATE [identity].[AppMenus]
   SET [IsActive] = 0
 WHERE [Code] IN ('admin-policy', 'admin-access-rev')
   AND [IsActive] = 1;

PRINT CONCAT('Soft-hidden ', @@ROWCOUNT, ' menu(s). Now flush Redis: redis-cli DEL menu-tree:*');
GO
