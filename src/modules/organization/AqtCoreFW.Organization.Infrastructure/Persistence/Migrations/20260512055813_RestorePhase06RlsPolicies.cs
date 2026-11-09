using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestorePhase06RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "organization", "OrgUnits", "StaffPositionHistories", "UserOrgUnitAssignments"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "organization", "OrgUnits", "StaffPositionHistories", "UserOrgUnitAssignments"))
                migrationBuilder.Sql(sql);
        }
    }
}
