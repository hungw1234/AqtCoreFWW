using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSignatureConfigSignFlowMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-tenant remote signature flow control: 0 = Async (default), 1 = Sync. Additive, non-null with a
            // store default so existing rows backfill to Async without an app-side data migration.
            migrationBuilder.AddColumn<int>(
                name: "SignFlowMode",
                schema: "identity",
                table: "TenantSignatureConfig",
                type: "int",
                nullable: false,
                defaultValueSql: "0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignFlowMode",
                schema: "identity",
                table: "TenantSignatureConfig");
        }
    }
}
