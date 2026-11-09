/* ============================================================================================
   Migration pre-deploy preflight check — read-only dirty-data probe for fail-loud migration guards
   Backlog: P1-STABLE-MIGRATION-PREDEPLOY-DRYRUN            Date: 2026-07-21
   ============================================================================================

   WHAT THIS DOES
   Read-only pre-deploy safety net. Some migrations carry an intentional fail-loud guard that
   THROWs inside the migration transaction when the DB holds data violating an invariant the
   migration is about to enforce. When that fires during deploy, `MigrateAsync` aborts and the
   API host cannot boot. This script reproduces those guard checks BEFORE deploy so operators can
   remediate the data first — instead of discovering the block mid-migrate.

   WHY (real prior incident)
   FORMS-DEV-DB-DUPLICATE-PUBLIC-CODE-MIGRATION-BLOCK (resolved 99fc5cb0): the Forms migration
   20260709082745_AddFormTemplatePublicCodeUniqueIndex THROWs 50000 when a public form Code is
   duplicated across tenants (IsPublic=1). A dev DB had `demo-intake-form` public in two tenants →
   the API host would not start. This script catches exactly that state ahead of the migrate step.

   HOW TO RUN (the -b flag is REQUIRED for exit-code semantics)
     sqlcmd -S <server> -d <database> -b -i scripts/260721-migration-preflight-check.sql

   `-b` makes sqlcmd abort the batch and return a NON-ZERO process exit code when the script
   RAISERRORs (severity 16) on dirty data. Without `-b` the message still PRINTs loudly, but the
   process exit code stays 0 and an automated ops step would miss the dirty state. Clean data
   returns exit code 0.

   REMEDIATION (Forms check): for each reported Code, demote all-but-one FormTemplate to
   IsPublic=0 (keep the single intended public row per Code), then re-run this script until CLEAN,
   then deploy. Non-destructive: `UPDATE forms.FormTemplates SET IsPublic=0 WHERE ...`.

   SAFE ON PROD: SELECT / PRINT / RAISERROR only. No INSERT / UPDATE / DELETE / DDL. Idempotent.

   MAINTENANCE: each guard check mirrors a specific migration's guard SQL verbatim. If a guard's
   SQL changes, update the mirrored block here too (the migration file path is cited above each
   check). Append a "Guard check #N" block when another fail-loud guard ships.
   ============================================================================================ */

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================================================
-- Guard check #1 — Forms public-Code cross-tenant duplication
-- Mirrors: src/modules/forms/.../Migrations/20260709082745_AddFormTemplatePublicCodeUniqueIndex.cs
--          (filtered unique index UX_FormTemplate_PublicCode, filter: [IsPublic]=1 AND [IsDeleted]=0)
-- ============================================================================================
IF OBJECT_ID(N'forms.FormTemplates', N'U') IS NULL
BEGIN
    -- Fresh / first-ever deploy: the table does not exist yet, so there is no data to violate the
    -- guard. Treat as clean.
    PRINT N'PREFLIGHT [Forms public-Code]: table forms.FormTemplates absent (first deploy) — nothing to check.';
END
ELSE
BEGIN
    DECLARE @dupes nvarchar(max);
    SELECT @dupes = STRING_AGG(CONVERT(nvarchar(max), Code), N', ')
    FROM (
        SELECT Code
        FROM forms.FormTemplates
        WHERE IsPublic = 1 AND IsDeleted = 0
        GROUP BY Code
        HAVING COUNT(*) > 1
    ) d;

    IF @dupes IS NOT NULL
    BEGIN
        -- Per-Code tenant breakdown so ops sees exactly which rows to demote.
        SELECT Code, COUNT(*) AS TenantCount
        FROM forms.FormTemplates
        WHERE IsPublic = 1 AND IsDeleted = 0
        GROUP BY Code
        HAVING COUNT(*) > 1;

        DECLARE @msg nvarchar(2048) =
            N'PREFLIGHT FAIL [Forms public-Code]: public Code(s) duplicated across tenants (IsPublic=1): '
            + @dupes
            + N'. Demote all-but-one FormTemplate to IsPublic=0 per Code before deploy, then re-run.';
        RAISERROR(@msg, 16, 1);  -- severity 16 + sqlcmd -b => non-zero process exit
    END
    ELSE
    BEGIN
        PRINT N'PREFLIGHT CLEAN [Forms public-Code]: no cross-tenant public Code duplicates. Safe to migrate.';
    END
END
GO
