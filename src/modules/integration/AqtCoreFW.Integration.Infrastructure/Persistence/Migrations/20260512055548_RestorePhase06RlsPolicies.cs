using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestorePhase06RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "integration", "Partners", "Contracts", "MessageLogs"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "integration", "Partners", "Contracts", "MessageLogs"))
                migrationBuilder.Sql(sql);
        }
    }
}
