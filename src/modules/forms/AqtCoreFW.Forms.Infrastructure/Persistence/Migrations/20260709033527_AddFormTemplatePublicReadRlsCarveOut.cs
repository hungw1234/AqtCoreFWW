using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Replaces the standard single-function FormTemplates RLS policy with a bespoke, asymmetric
    /// two-function policy that adds a flag-gated public-read carve-out.
    ///
    /// FILTER predicate (SELECT): the standard tenant match PLUS
    /// <c>(IsPublic = 1 AND public_forms_bypass = 'true')</c>, so an anonymous (no-tenant) caller
    /// resolving a public template by Code sees the row under a non-db_owner login — but only on a
    /// connection where the server explicitly set the <c>public_forms_bypass</c> session flag.
    ///
    /// BLOCK predicate (AFTER INSERT): a SEPARATE tenant-only function with NO carve-out, so the
    /// bypass flag can never influence an INSERT — a write is only ever admitted when the row's
    /// TenantId matches session context (or db_owner). This mirrors the asymmetric audit-admin-bypass
    /// policy (RlsMigrationHelper.NullableTenant.cs). The flag is set exclusively on the read bypass
    /// connection, so this is defence-in-depth, not a live gap.
    ///
    /// Policy/FILTER-function names are unchanged (pol_forms_FormTemplates_Tenant /
    /// fn_forms_FormTemplates_TenantFilter) so the RLS policy-presence matrix stays exact.
    /// </summary>
    public partial class AddFormTemplatePublicReadRlsCarveOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the original single-function policy + fn_forms_FormTemplates_TenantFilter
            //    created by RestorePhase06RlsPolicies (leaves FormSubmissions/ViewDefinitions untouched).
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements("forms", "FormTemplates"))
                migrationBuilder.Sql(sql);

            // 2. FILTER function (2-arg): standard tenant match + flag-gated public-read carve-out.
            //    public_forms_bypass is set only server-side on a dedicated read connection; no
            //    @read_only=1, so sp_reset_connection clears it on pool return (no cross-connection leak).
            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION [forms].[fn_forms_FormTemplates_TenantFilter]
                    (@TenantId uniqueidentifier, @IsPublic bit)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                (
                    SELECT 1 AS fn_result
                    WHERE
                        -- Allow db_owner / sa (migration tooling, admin batch jobs)
                        IS_MEMBER('db_owner') = 1
                        OR
                        -- Match session TenantId set by TenantSessionContextInterceptor
                        @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                        OR
                        -- Public-read carve-out: an anonymous by-Code resolution sees an IsPublic=1
                        -- row only when the server set public_forms_bypass='true' on this connection.
                        (@IsPublic = 1 AND TRY_CAST(SESSION_CONTEXT(N'public_forms_bypass') AS bit) = 1)
                )
                """);

            // 3. BLOCK function (1-arg, asymmetric): tenant-only, NO carve-out. The bypass flag can
            //    never admit a cross-tenant INSERT — writes require the row TenantId to match session.
            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION [forms].[fn_forms_FormTemplates_TenantInsertGuard]
                    (@TenantId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                (
                    SELECT 1 AS fn_result
                    WHERE
                        IS_MEMBER('db_owner') = 1
                        OR
                        @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
                )
                """);

            // 4. Security policy: FILTER binds (TenantId, IsPublic); BLOCK binds (TenantId) only.
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY [forms].[pol_forms_FormTemplates_Tenant]
                ADD FILTER PREDICATE [forms].[fn_forms_FormTemplates_TenantFilter](TenantId, IsPublic)
                    ON [forms].[FormTemplates],
                ADD BLOCK PREDICATE [forms].[fn_forms_FormTemplates_TenantInsertGuard](TenantId)
                    ON [forms].[FormTemplates] AFTER INSERT
                WITH (STATE = ON, SCHEMABINDING = ON)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the bespoke asymmetric policy + both functions (policy references both).
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS [forms].[pol_forms_FormTemplates_Tenant]");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [forms].[fn_forms_FormTemplates_TenantFilter]");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [forms].[fn_forms_FormTemplates_TenantInsertGuard]");

            // Restore the original standard single-function policy for FormTemplates only.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements("forms", "FormTemplates"))
                migrationBuilder.Sql(sql);
        }
    }
}
