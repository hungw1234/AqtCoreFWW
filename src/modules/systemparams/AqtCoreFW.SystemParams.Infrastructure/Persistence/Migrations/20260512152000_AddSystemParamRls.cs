using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.SystemParams.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds symmetric RLS policy to config.SystemParameters.
    ///
    /// SystemParameters rows can be:
    ///   TenantId = NULL       → global/system parameter visible to ALL tenants (read)
    ///   TenantId = &lt;string&gt;  → tenant-specific override, visible only to that tenant
    ///
    /// Design (PHASE6-RLS-NULLABLE-TENANT Option A, Phase 04):
    ///   One TVF (fn_config_SystemParameters_TenantFilter) serves FILTER,
    ///   BLOCK-AFTER-INSERT, and BLOCK-AFTER-UPDATE — symmetric design.
    ///
    ///   FILTER predicate:
    ///     IS_MEMBER('db_owner')=1
    ///     OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(100))
    ///     OR @TenantId IS NULL   ← global parameters visible to all tenants
    ///
    ///   BLOCK predicates (same function, AFTER INSERT + AFTER UPDATE):
    ///     NULL-tenant rows allowed — SystemParamSeeder writes global params at startup
    ///     without a session tenant context set; blocking would break startup seeding.
    ///     Tenant-specific rows require the row TenantId to match the session TenantId.
    ///
    /// Compensating controls for unrestricted NULL-tenant writes:
    ///   - IsEditable=false on system-managed params (SystemParameter.UpdateValue throws)
    ///   - UpdateSystemParamCommandHandler authorization gate
    ///
    /// PHASE6-RLS-NULLABLE-TENANT plan reference:
    ///   plans/260512-1351-phase6-rls-nullable-tenant/phase-04-systemparams-module.md
    /// </summary>
    public partial class AddSystemParamRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateSystemParamRlsPolicyStatements(
                "config", "SystemParameters"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropSystemParamRlsPolicyStatements(
                "config", "SystemParameters"))
                migrationBuilder.Sql(sql);
        }
    }
}
