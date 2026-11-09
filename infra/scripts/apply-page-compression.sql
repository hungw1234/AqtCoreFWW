-- apply-page-compression.sql
-- Phase-06/6C — Apply DATA_COMPRESSION = PAGE on all heap/index objects in app schemas.
-- Idempotent — safe to re-run; only touches indexes whose data_compression = 0 (NONE).
--
-- Run after `dotnet ef database update` (or host startup MigrateAsync) on a fresh dev DB.
-- Per memory `feedback-sqlcmd-quoted-identifier` we MUST set QUOTED_IDENTIFIER + ANSI_NULLS
-- because sqlcmd defaults differ from Mgmt Studio and EF entities use filtered indexes.
--
-- Schema whitelist drives from 19 module/shared DbContexts (verified 2026-05-05):
--   ai, alerting, audit, backup, collab, config, files, forms, gateway, identity,
--   integration, masterdata, messaging, notifications, organization, reporting,
--   rules, webhooks, workflow, dbo (root).
-- Excluded: hangfire (framework-managed), OpenIddict* tables under identity schema.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql = @sql + N'ALTER INDEX ALL ON '
    + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
    + N' REBUILD WITH (DATA_COMPRESSION = PAGE);' + CHAR(13) + CHAR(10)
FROM sys.tables t
WHERE t.is_ms_shipped = 0
  AND SCHEMA_NAME(t.schema_id) IN (
        N'ai', N'alerting', N'audit', N'backup', N'collab', N'config',
        N'files', N'forms', N'gateway', N'identity', N'integration',
        N'masterdata', N'messaging', N'notifications', N'organization',
        N'reporting', N'rules', N'webhooks', N'workflow', N'dbo'
      )
  AND SCHEMA_NAME(t.schema_id) <> N'hangfire'
  AND t.name NOT LIKE 'OpenIddict%'
  AND EXISTS (
        SELECT 1 FROM sys.indexes i
        INNER JOIN sys.partitions p ON p.object_id = i.object_id AND p.index_id = i.index_id
        WHERE i.object_id = t.object_id
          AND p.data_compression = 0
      )
GROUP BY t.schema_id, t.name;

IF LEN(@sql) = 0
BEGIN
    PRINT '==> No indexes require page compression (already applied or no qualifying tables).';
    RETURN;
END;

PRINT '==> Applying DATA_COMPRESSION = PAGE on:';
PRINT @sql;

EXEC sp_executesql @sql;

PRINT '==> Page compression apply complete.';
