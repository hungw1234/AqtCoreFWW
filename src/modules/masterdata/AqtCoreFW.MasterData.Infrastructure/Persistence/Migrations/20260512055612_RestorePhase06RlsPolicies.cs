using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestorePhase06RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "masterdata", "Dictionaries", "DictionaryItems", "ExternalMappings"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "masterdata", "Dictionaries", "DictionaryItems", "ExternalMappings"))
                migrationBuilder.Sql(sql);
        }
    }
}
