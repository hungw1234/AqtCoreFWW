-- 260423-soft-hide-sod-rules.sql
-- Version: v2.128p3 (2026-04-23)
-- Purpose: Soft-hide menu item "admin-sod" (/admin/sod-rules) trong sidebar,
--          đồng bộ với hide intent FE admin-menu-entries.ts + BE seeder comments.
--
-- Background:
--   Feature SoD Rules vẫn hoạt động đầy đủ (module Identity, T4-06 v2.127 refactor hoàn tất).
--   Chỉ ẩn MENU khỏi sidebar — route + BE endpoints + permissions giữ nguyên.
--   Admin có thể truy cập qua URL /admin/sod-rules trực tiếp với permission IDENTITY.SOD.VIEW.
--
-- Strategy (Option B — soft-hide, giống v2.127p9 pattern):
--   UPDATE IsActive=0 thay vì DELETE. Admin /admin/menus vẫn thấy row
--   với cột "Kích hoạt: Không". Bật lại = UPDATE IsActive=1.
--
-- Affected row (idempotent — re-run an toàn):
--   - admin-sod  (Id 60000000-0000-0000-0000-000000000055, perm IDENTITY.SOD.VIEW)
--
-- Required cleanup AFTER this script:
--   docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' | xargs -I {} docker exec docker-redis-1 redis-cli DEL {}
--
-- Rollback (bật lại):
--   UPDATE [identity].[AppMenus] SET [IsActive] = 1 WHERE [Code] = 'admin-sod';
--   + flush Redis menu-tree:* như trên.

USE [AqtCoreFW];
GO

-- SET QUOTED_IDENTIFIER ON — required by indexed views / computed columns / filtered indexes
-- on AppMenus (SQL Server raises Msg 1934 otherwise).
SET QUOTED_IDENTIFIER ON;
GO

UPDATE [identity].[AppMenus]
   SET [IsActive] = 0
 WHERE [Code] = 'admin-sod'
   AND [IsActive] = 1;

PRINT CONCAT('Soft-hidden ', @@ROWCOUNT, ' menu(s). Now flush Redis: redis-cli DEL menu-tree:*');
GO
