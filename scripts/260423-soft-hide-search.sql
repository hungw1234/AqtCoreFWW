-- 260423-soft-hide-search.sql
-- Version: v2.128p2 (2026-04-23)
-- Purpose: Soft-hide menu item "search" (/search) trong DB runtime,
--          cleanup orphan còn sót sau PR #45 (xóa AqtCoreFW.Search BE module dead code).
--
-- Background:
--   - PR #45 (449b18fa, 2026-04-20) xóa BE module AqtCoreFW.Search.
--   - aa573033 clean up orphans nhưng bỏ sót seeder entry MenuSearchId.
--   - v2.128p1 (this commit) comment seeder + soft-hide DB runtime.
--
-- Strategy (Option B — soft-hide):
--   UPDATE IsActive=0 thay vì DELETE để dễ rollback. Admin /admin/menus
--   vẫn thấy row với cột "Kích hoạt: Không". Bật lại = UPDATE IsActive=1
--   (nhưng phải khôi phục BE Search module trước — hiện module đã bị xóa).
--
-- Affected row (idempotent — re-run an toàn):
--   - search  (Id 60000000-0000-0000-0000-00000000000C, perm SEARCH.QUERY.USE)
--
-- Required cleanup AFTER this script:
--   docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' | xargs -I {} docker exec docker-redis-1 redis-cli DEL {}
--
-- Rollback (bật lại — chỉ khi BE Search module được khôi phục):
--   UPDATE [identity].[AppMenus] SET [IsActive] = 1 WHERE [Code] = 'search';
--   + flush Redis menu-tree:* như trên.

USE [AqtCoreFW];
GO

-- SET QUOTED_IDENTIFIER ON — required by indexed views / computed columns / filtered indexes
-- on AppMenus (SQL Server raises Msg 1934 otherwise).
SET QUOTED_IDENTIFIER ON;
GO

UPDATE [identity].[AppMenus]
   SET [IsActive] = 0
 WHERE [Code] = 'search'
   AND [IsActive] = 1;

PRINT CONCAT('Soft-hidden ', @@ROWCOUNT, ' menu(s). Now flush Redis: redis-cli DEL menu-tree:*');
GO
