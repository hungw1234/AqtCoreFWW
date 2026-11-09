-- =============================================================================
-- drop-cases-schema.sql
-- Post-deploy ops script: removes Cases module schema and orphan rows from
-- shared tables (permissions, menus, attribute rules).
--
-- Context : Phase 05 of Cases module removal
--           Plan    : plans/260501-1152-full-delete-cases-module/phase-05-db-drop-migration.md
--           PR      : worktree-remove-cases-module (see chore(cases) commit series)
--
-- IMPORTANT : Run AFTER deploying the BE with Cases module removed.
--             Run as a user with db_owner or sysadmin role.
--             NEVER deletes audit.AuditLogs (QĐ 742 ATTT compliance — immutable).
--             Safe to re-run (idempotent): all statements guarded by IF EXISTS.
-- =============================================================================

SET NOCOUNT ON;
-- QUOTED_IDENTIFIER + ANSI_NULLS must be ON to write to tables that have
-- computed columns / filtered indexes / indexed views (e.g. reporting.ReportDefinitions).
-- sqlcmd defaults QUOTED_IDENTIFIER OFF — without this guard the second BEGIN TRY
-- below fails with Msg 1934.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
PRINT '=== drop-cases-schema.sql START ===';
PRINT 'Run time: ' + CONVERT(varchar, GETUTCDATE(), 120) + ' UTC';
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- -------------------------------------------------------------------------
    -- 1. Remove FK rows: identity.RolePermissions → identity.Permissions
    -- -------------------------------------------------------------------------
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'identity' AND TABLE_NAME = 'RolePermissions'
    )
    BEGIN
        DECLARE @rpDeleted int;
        DELETE rp
        FROM [identity].[RolePermissions] rp
        INNER JOIN [identity].[Permissions] p ON rp.[PermissionId] = p.[Id]
        WHERE p.[Code] LIKE 'CASES.%';
        SET @rpDeleted = @@ROWCOUNT;
        PRINT 'identity.RolePermissions deleted: ' + CAST(@rpDeleted AS varchar);
    END
    ELSE
        PRINT 'SKIP: identity.RolePermissions table not found';

    -- -------------------------------------------------------------------------
    -- 2. Remove orphan permission rows
    -- -------------------------------------------------------------------------
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'identity' AND TABLE_NAME = 'Permissions'
    )
    BEGIN
        DECLARE @pDeleted int;
        DELETE FROM [identity].[Permissions]
        WHERE [Code] LIKE 'CASES.%';
        SET @pDeleted = @@ROWCOUNT;
        PRINT 'identity.Permissions deleted: ' + CAST(@pDeleted AS varchar);
    END
    ELSE
        PRINT 'SKIP: identity.Permissions table not found';

    -- -------------------------------------------------------------------------
    -- 3. Remove Case attribute rules (ABAC)
    -- -------------------------------------------------------------------------
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'identity' AND TABLE_NAME = 'AttributeRules'
    )
    BEGIN
        DECLARE @arDeleted int;
        DELETE FROM [identity].[AttributeRules]
        WHERE [Resource] = 'Case';
        SET @arDeleted = @@ROWCOUNT;
        PRINT 'identity.AttributeRules deleted: ' + CAST(@arDeleted AS varchar);
    END
    ELSE
        PRINT 'SKIP: identity.AttributeRules table not found';

    -- -------------------------------------------------------------------------
    -- 4. Remove Cases menu entry
    --    MenuCasesId = 60000000-0000-0000-0000-000000000002 (E2ETestDataSeeder)
    --    Belt-and-suspenders: also match by Code = 'cases'
    -- -------------------------------------------------------------------------
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'identity' AND TABLE_NAME = 'AppMenus'
    )
    BEGIN
        DECLARE @mDeleted int;
        DELETE FROM [identity].[AppMenus]
        WHERE [Id] = '60000000-0000-0000-0000-000000000002'
           OR [Code] = 'cases';
        SET @mDeleted = @@ROWCOUNT;
        PRINT 'identity.AppMenus deleted: ' + CAST(@mDeleted AS varchar);
    END
    ELSE
        PRINT 'SKIP: identity.AppMenus table not found';

    -- -------------------------------------------------------------------------
    -- 5-12. Drop cases schema tables and schema (only if schema exists)
    -- -------------------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'cases')
    BEGIN
        PRINT 'Dropping cases schema objects...';

        -- 5. CaseComments (FK → Cases)
        IF OBJECT_ID('cases.CaseComments', 'U') IS NOT NULL
        BEGIN
            DROP TABLE [cases].[CaseComments];
            PRINT '  Dropped: cases.CaseComments';
        END

        -- 6. CaseWorkflow (FK → Cases)
        IF OBJECT_ID('cases.CaseWorkflow', 'U') IS NOT NULL
        BEGIN
            DROP TABLE [cases].[CaseWorkflow];
            PRINT '  Dropped: cases.CaseWorkflow';
        END

        -- 7. Cases (root entity)
        IF OBJECT_ID('cases.Cases', 'U') IS NOT NULL
        BEGIN
            DROP TABLE [cases].[Cases];
            PRINT '  Dropped: cases.Cases';
        END

        -- 8. CaseCounters
        IF OBJECT_ID('cases.CaseCounters', 'U') IS NOT NULL
        BEGIN
            DROP TABLE [cases].[CaseCounters];
            PRINT '  Dropped: cases.CaseCounters';
        END

        -- 9. Sequences (dynamic — enumerate sys.sequences in schema)
        DECLARE @seqName nvarchar(256);
        DECLARE @dropSeqSql nvarchar(512);
        DECLARE seq_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT s.name
            FROM sys.sequences s
            INNER JOIN sys.schemas sc ON s.schema_id = sc.schema_id
            WHERE sc.name = 'cases';
        OPEN seq_cursor;
        FETCH NEXT FROM seq_cursor INTO @seqName;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @dropSeqSql = N'DROP SEQUENCE [cases].[' + @seqName + N'];';
            EXEC sp_executesql @dropSeqSql;
            PRINT '  Dropped sequence: cases.' + @seqName;
            FETCH NEXT FROM seq_cursor INTO @seqName;
        END;
        CLOSE seq_cursor;
        DEALLOCATE seq_cursor;

        -- 10. OutboxMessages (schema-local outbox, no FK to Cases)
        IF OBJECT_ID('cases.OutboxMessages', 'U') IS NOT NULL
        BEGIN
            DROP TABLE [cases].[OutboxMessages];
            PRINT '  Dropped: cases.OutboxMessages';
        END

        -- 11. EF Migrations history (schema-local)
        IF OBJECT_ID('cases.__EFMigrationsHistory', 'U') IS NOT NULL
        BEGIN
            DROP TABLE [cases].[__EFMigrationsHistory];
            PRINT '  Dropped: cases.__EFMigrationsHistory';
        END

        -- 12. Drop schema itself (must be empty)
        DROP SCHEMA [cases];
        PRINT '  Dropped SCHEMA: cases';
    END
    ELSE
        PRINT 'SKIP: schema ''cases'' does not exist (already dropped or never migrated)';

    COMMIT TRANSACTION;
    PRINT '=== drop-cases-schema.sql COMPLETE (transaction committed) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @errMsg nvarchar(2048) = ERROR_MESSAGE();
    DECLARE @errLine int = ERROR_LINE();
    PRINT '!!! ERROR at line ' + CAST(@errLine AS varchar) + ': ' + @errMsg;
    THROW;
END CATCH;
GO

-- =============================================================================
-- Reporting module purge (PR #71 cleanup — H1 fix)
-- Purge any ReportDefinitions / QueryCatalog rows seeded by the now-deleted
-- ReportDefinitionSeeder that referenced cases.Cases SQL templates.
-- Safe to re-run: DELETE is idempotent on non-existent rows.
-- =============================================================================

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('reporting.ReportDefinitions', 'U') IS NOT NULL
    BEGIN
        DELETE FROM [reporting].[ReportDefinitions]
        WHERE Name = 'CasesStatisticsReport';
        PRINT 'Purged reporting.ReportDefinitions WHERE Name=CasesStatisticsReport (rows: ' + CAST(@@ROWCOUNT AS varchar) + ')';
    END
    ELSE
        PRINT 'SKIP: reporting.ReportDefinitions does not exist';

    IF OBJECT_ID('reporting.QueryCatalog', 'U') IS NOT NULL
    BEGIN
        DELETE FROM [reporting].[QueryCatalog]
        WHERE Code IN (
            'SLA_BY_DEPARTMENT_PERIOD',
            'CASE_COUNT_BY_TYPE',
            'OFFICER_PERFORMANCE',
            'OVERDUE_TREND',
            'BOTTLENECK_ANALYSIS'
        );
        PRINT 'Purged reporting.QueryCatalog CASES_* entries (rows: ' + CAST(@@ROWCOUNT AS varchar) + ')';
    END
    ELSE
        PRINT 'SKIP: reporting.QueryCatalog does not exist';

    COMMIT TRANSACTION;
    PRINT '=== Reporting purge COMPLETE ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @rptErrMsg nvarchar(2048) = ERROR_MESSAGE();
    DECLARE @rptErrLine int = ERROR_LINE();
    PRINT '!!! Reporting purge ERROR at line ' + CAST(@rptErrLine AS varchar) + ': ' + @rptErrMsg;
    THROW;
END CATCH;
GO

-- =============================================================================
-- Verification queries (run after script to confirm clean state)
-- =============================================================================
-- SELECT * FROM sys.schemas WHERE name = 'cases';                   -- expect 0 rows
-- SELECT COUNT(*) FROM [identity].[Permissions] WHERE Code LIKE 'CASES.%';  -- expect 0
-- SELECT COUNT(*) FROM [identity].[RolePermissions] rp
--   INNER JOIN [identity].[Permissions] p ON rp.PermissionId = p.Id
--   WHERE p.Code LIKE 'CASES.%';                                    -- expect 0
-- SELECT COUNT(*) FROM [identity].[AttributeRules] WHERE Resource = 'Case';    -- expect 0
-- SELECT COUNT(*) FROM [identity].[AppMenus] WHERE Code = 'cases';  -- expect 0
-- SELECT COUNT(*) FROM [reporting].[ReportDefinitions] WHERE Name = 'CasesStatisticsReport'; -- expect 0
-- SELECT COUNT(*) FROM [reporting].[QueryCatalog] WHERE Code LIKE 'CASE%' OR Code = 'SLA_BY_DEPARTMENT_PERIOD' OR Code = 'OFFICER_PERFORMANCE' OR Code = 'OVERDUE_TREND' OR Code = 'BOTTLENECK_ANALYSIS'; -- expect 0
-- =============================================================================
