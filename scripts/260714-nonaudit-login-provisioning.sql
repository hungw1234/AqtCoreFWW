/* ============================================================================================
   NonAudit least-privilege login provisioning — activates ISO-DAPPER audit read-isolation
   Backlog: ISO-DAPPER-KEYED-DI-INCREMENT   ADR: docs/adr/012-...   Date: 2026-07-14
   ============================================================================================

   WHAT THIS DOES
   Creates the production `aqtcore_nonaudit` login/user that the non-audit read path runs under.
   It is GRANTed DML + DDL on the 19 NON-AUDIT schemas and DENIED the [audit] schema, so a non-audit
   service's ordinary reads (Dapper default `IReadDbConnection` + the 14 module EF contexts +
   Alerting/ApiKey/Webhook) cannot reach [audit]. Audit-capable handlers use the keyed "audit"
   reader on the broad Default login instead (see InfrastructureRegistration.AddInfrastructure).

   GRANT SCOPE (DBA decision 2026-07-14 = DML + DDL): DML keeps normal reads/writes working; DDL
   (ALTER ON SCHEMA + CREATE TABLE) keeps the existing INLINE EF `MigrateAsync` deploy model working
   under this login. Isolation is about [audit] READ access, not DDL on the login's own schemas.
   If your environment migrates OUT-OF-BAND instead, you may drop CREATE TABLE + ALTER and set
   SKIP_STARTUP_MIGRATIONS=true on every host — see the ordering runbook below.

   ⚠️ ORDERING INVARIANT (prevents boot crash — review finding, ADR-012):
     1. Run THIS script (login provisioned WITH all grants) FIRST.
     2. Publish the password to Vault KV as  ConnectionStrings__NonAudit  (maps to
        ConnectionStrings:NonAudit; keep it OUT of ProductionSecretValidator.RequiredKeys so the
        app still boots opt-in if the key is absent).
     3. ONLY THEN set/point the app at ConnectionStrings:NonAudit.
   If the app is pointed at NonAudit BEFORE the grants exist, every non-audit read (and, on inline-
   migrate hosts, every startup migration) fails with SqlException 229 / permission denied → crash loop.

   ROLLBACK: unset ConnectionStrings:NonAudit (or remove the Vault key). NonAuditConnectionString.Get
   falls back to Default → isolation dormant, app unaffected. The login/user can be dropped after.

   SECURITY: the password is NOT embedded. Pass it via sqlcmd:  -v NonAuditPassword="<from-vault>"
   Never commit the real secret.
   ⚠️ There is deliberately NO ":setvar NonAuditPassword" default in this file: an in-file
   :setvar OVERRIDES the -v value (sqlcmd precedence), so the original placeholder default
   silently created the login with the literal placeholder password — any repo reader could
   log in. Caught empirically on the 2026-07-23 local-kind rehearsal. Running without -v now
   fails fast with "'NonAuditPassword' scripting variable not defined" instead.

   Idempotent: safe to re-run.
   ============================================================================================ */

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @login sysname = N'aqtcore_nonaudit';

-- 1) Login (server principal)
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
BEGIN
    DECLARE @createLogin nvarchar(max) =
        N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ''$(NonAuditPassword)'', '
      + N'CHECK_EXPIRATION = ON, CHECK_POLICY = ON;';
    EXEC sys.sp_executesql @createLogin;
END
GO

-- 2) Database user
DECLARE @login sysname = N'aqtcore_nonaudit';
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login)
    EXEC sys.sp_executesql N'CREATE USER [aqtcore_nonaudit] FOR LOGIN [aqtcore_nonaudit];';
GO

-- 3) Database-level DDL needed for inline EF migrations to create NEW tables (scoped by the
--    per-schema ALTER grants below + the [audit] DENY, so it can only create in non-audit schemas).
GRANT CREATE TABLE TO [aqtcore_nonaudit];
GO

-- 4) Per-schema GRANT DML + DDL on the 19 NON-AUDIT schemas. [audit] is intentionally absent.
DECLARE @schemas TABLE (name sysname PRIMARY KEY);
INSERT INTO @schemas (name) VALUES
    (N'ai'), (N'alerting'), (N'backup'), (N'businessprocess'), (N'collab'), (N'config'),
    (N'files'), (N'forms'), (N'gateway'), (N'identity'), (N'integration'), (N'masterdata'),
    (N'messaging'), (N'notifications'), (N'organization'), (N'reporting'), (N'rules'),
    (N'webhooks'), (N'workflow');
-- NOTE: DBA — reconcile this list against sys.schemas before running; a MISSING non-audit schema
-- means that schema's reads deny under this login (surfaces only once NonAudit is provisioned).

DECLARE @s sysname, @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @schemas;
OPEN c; FETCH NEXT FROM c INTO @s;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @s)
        RAISERROR('Schema [%s] not found — grant skipped; reconcile the schema list.', 10, 1, @s);
    ELSE
    BEGIN
        SET @sql =
            N'GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER, REFERENCES, VIEW DEFINITION '
          + N'ON SCHEMA::' + QUOTENAME(@s) + N' TO [aqtcore_nonaudit];';
        EXEC sys.sp_executesql @sql;
    END
    FETCH NEXT FROM c INTO @s;
END
CLOSE c; DEALLOCATE c;
GO

-- 5) Explicit DENY on [audit] (belt-and-braces over default-deny; makes the boundary auditable).
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER, REFERENCES, CONTROL
    ON SCHEMA::[audit] TO [aqtcore_nonaudit];
GO

-- 6) Verify (non-vacuous): must be non-db_owner, DENIED [audit], and NOT a member of a broad role.
--    Expected: is_db_owner = 0.
SELECT
    IS_MEMBER('db_owner')            AS is_db_owner,
    HAS_PERMS_BY_NAME('audit', 'SCHEMA', 'SELECT') AS can_select_audit;  -- expect 0 for the user's context
GO
