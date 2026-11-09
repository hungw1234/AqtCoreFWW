-- 260422-soft-hide-consent-rtbf.sql
-- Version: v2.127p9 (2026-04-22)
-- Purpose: Soft-hide menu items "consent" + "admin-rtbf" trong DB runtime,
--          đồng bộ với hide intent v2.127p4 (RTBF) + v2.127p5 (consent) + v2.127p8 (seeder).
--
-- Background:
--   - v2.127p4/p5 ẩn FE hardcoded fallback (chỉ hiệu lực khi API rỗng).
--   - v2.127p8 ẩn BE seeder (chỉ áp dụng fresh DB, không reseed).
--   - DB runtime hiện có vẫn IsActive=1 → sidebar serve menu cho user có quyền.
--
-- Strategy (Option B — soft-hide):
--   UPDATE IsActive=0 thay vì DELETE để dễ rollback. Admin /admin/menus
--   vẫn thấy row với cột "Kích hoạt: Không". Bật lại = UPDATE IsActive=1.
--
-- Affected rows (idempotent — re-run an toàn):
--   - admin-rtbf  (Id 60000000-0000-0000-0000-000000000076, perm AUDIT.RTBF.VIEW)
--   - consent     (Id 60000000-0000-0000-0000-000000000010, perm IDENTITY.CONSENT.VIEW)
--
-- Required cleanup AFTER this script:
--   docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' | xargs -I {} docker exec docker-redis-1 redis-cli DEL {}
--
-- Rollback (bật lại):
--   UPDATE [identity].[AppMenus] SET [IsActive] = 1 WHERE [Code] IN ('admin-rtbf','consent');
--   + flush Redis menu-tree:* như trên.

USE [AqtCoreFW];
GO

-- SET QUOTED_IDENTIFIER ON — required by indexed views / computed columns / filtered indexes
-- on AppMenus (SQL Server raises Msg 1934 otherwise).
SET QUOTED_IDENTIFIER ON;
GO

UPDATE [identity].[AppMenus]
   SET [IsActive] = 0
 WHERE [Code] IN ('admin-rtbf', 'consent')
   AND [IsActive] = 1;

PRINT CONCAT('Soft-hidden ', @@ROWCOUNT, ' menu(s). Now flush Redis: redis-cli DEL menu-tree:*');
GO
