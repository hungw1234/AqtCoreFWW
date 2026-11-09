using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds SQL Server Row-Level Security to the Tier-3 doc-flow tables (SigningDocFlowRecords,
    /// SigningDocFlowNonces) so a tenant can never read or write another tenant's signing rows at the
    /// DB engine level — defence in depth behind the explicit tenant assertions in the handler.
    ///
    /// Both tables carry a non-nullable uniqueidentifier TenantId, so the standard symmetric policy
    /// applies (FILTER on SELECT + BLOCK AFTER INSERT). The predicate honours IS_MEMBER('db_owner')
    /// for migration/admin tooling and otherwise matches SESSION_CONTEXT(N'TenantId') — set by the
    /// TenantSessionContextInterceptor for authenticated requests and, for the [AllowAnonymous] VGCA
    /// callback, explicitly from the signed JWT tenant_id by FileUploadHandlerController.
    ///
    /// No model change (RLS is pure DDL), but a companion .Designer.cs is still REQUIRED: EF discovers
    /// migrations by the [Migration] attribute, which lives in the Designer. Without it EF silently
    /// skips the migration and the policy is never created. Its BuildTargetModel is identical to the
    /// snapshot (no model delta) — same as every other RLS migration (RestorePhase06RlsPolicies etc.).
    /// </summary>
    public partial class AddSigningDocFlowRlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "SigningDocFlowRecords", "SigningDocFlowNonces"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "SigningDocFlowRecords", "SigningDocFlowNonces"))
                migrationBuilder.Sql(sql);
        }
    }
}
