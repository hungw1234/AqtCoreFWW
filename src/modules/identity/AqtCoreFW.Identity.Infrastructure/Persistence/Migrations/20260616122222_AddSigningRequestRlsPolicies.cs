using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds SQL Server Row-Level Security to the SigningRequests workflow table so a tenant can never read or
    /// write another tenant's signing requests at the DB engine level — defence in depth behind the explicit
    /// tenant predicate the orchestrator applies on every load.
    ///
    /// SigningRequests carries a non-nullable uniqueidentifier TenantId, so the standard symmetric policy
    /// applies (FILTER on SELECT + BLOCK AFTER INSERT). The predicate honours IS_MEMBER('db_owner') for
    /// migration/admin tooling and otherwise matches SESSION_CONTEXT(N'TenantId') set by the tenant interceptor.
    ///
    /// Pure DDL (no model delta), but the companion .Designer.cs is still required — EF discovers migrations by
    /// the [Migration] attribute that lives in the Designer; its BuildTargetModel equals the snapshot.
    /// </summary>
    public partial class AddSigningRequestRlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "SigningRequests"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "SigningRequests"))
                migrationBuilder.Sql(sql);
        }
    }
}
