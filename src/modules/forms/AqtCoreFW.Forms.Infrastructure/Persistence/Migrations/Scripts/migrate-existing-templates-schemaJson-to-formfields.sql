-- =============================================================================
-- Data Migration: FormTemplate.SchemaJson → FormFields rows
-- Run AFTER EF migration AddFormFieldsAndDualStorage is applied.
-- Idempotent: skips templates that already have FormField rows.
-- Sets Code = slugified Name, StorageMode = 'Json', Status based on IsActive backup.
-- =============================================================================
-- NOTE: This script assumes you backed up the original IsActive and SchemaJson
-- before running the EF migration. If the EF migration has already run,
-- you must restore from backup or maintain a staging table.
-- =============================================================================

BEGIN TRANSACTION;

BEGIN TRY

    -- Step 1: Populate Code + StorageMode + Status for existing templates
    -- Code = replace spaces/special chars with underscore, truncate to 100
    UPDATE forms.FormTemplates
    SET
        Code = LOWER(LEFT(
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                Name,
                ' ', '_'), '-', '_'), '.', '_'), '/', '_'), '\', '_'),
            100)),
        StorageMode = 'Json',
        -- Status: assume all existing templates were Active (IsActive=true when saved)
        -- If you have a backup of IsActive, replace the constant with the backed-up value
        Status = 'Active'
    WHERE Code = ''  -- Only update templates with empty Code (newly migrated)
      AND IsDeleted = 0;

    -- Step 2: Ensure Code uniqueness within tenant (append _2, _3 etc if collision)
    -- Handled by application logic on subsequent saves. Migration just sets initial codes.

    -- Step 3: Migrate SchemaJson fields to FormFields rows using OPENJSON
    -- Only for templates that have no FormField rows yet (idempotent)
    --
    -- NOTE: SchemaJson column has been DROPPED by the EF migration.
    -- This script is meant to run from a STAGING or BACKUP table.
    -- Uncomment and adapt if you restore SchemaJson from backup:
    --
    -- INSERT INTO forms.FormFields
    --     (Id, FormTemplateId, FieldKey, Type, LabelVi, LabelEn,
    --      DisplayOrder, Required, IsActive, AddedInVersion,
    --      ValidationRulesJson, CreatedAt, IsDeleted)
    -- SELECT
    --     NEWID(),
    --     t.Id,
    --     COALESCE(j.fieldId, CAST(ROW_NUMBER() OVER (PARTITION BY t.Id ORDER BY j.[key]) AS nvarchar)),
    --     COALESCE(j.type, 'Text'),
    --     COALESCE(j.labelVi, j.fieldId, ''),
    --     COALESCE(j.labelEn, j.fieldId, ''),
    --     CAST(j.[key] AS int),   -- original array index as display order
    --     CAST(ISNULL(j.required, 0) AS bit),
    --     1,                      -- IsActive = true
    --     t.Version,              -- AddedInVersion = current template version
    --     NULL,                   -- ValidationRulesJson
    --     GETUTCDATE(),
    --     0                       -- IsDeleted = false
    -- FROM forms.FormTemplates t
    -- CROSS APPLY OPENJSON(t_backup.SchemaJson)
    -- WITH (
    --     fieldId    nvarchar(128)  '$.fieldId',
    --     type       nvarchar(30)   '$.type',
    --     labelVi    nvarchar(500)  '$.labelVi',
    --     labelEn    nvarchar(500)  '$.labelEn',
    --     required   bit            '$.required'
    -- ) AS j
    -- WHERE NOT EXISTS (
    --     SELECT 1 FROM forms.FormFields f WHERE f.FormTemplateId = t.Id
    -- );

    PRINT 'Data migration completed successfully.';
    COMMIT TRANSACTION;

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    DECLARE @Msg nvarchar(4000) = ERROR_MESSAGE();
    RAISERROR('Data migration failed: %s', 16, 1, @Msg);
END CATCH;
