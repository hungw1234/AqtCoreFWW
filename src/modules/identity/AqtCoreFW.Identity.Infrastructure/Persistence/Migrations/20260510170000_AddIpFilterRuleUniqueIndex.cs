using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIpFilterRuleUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IpFilterRule_Cidr",
                schema: "identity",
                table: "IpFilterRules");

            migrationBuilder.CreateIndex(
                name: "UQ_IpFilterRule_TenantId_Cidr",
                schema: "identity",
                table: "IpFilterRules",
                columns: new[] { "TenantId", "Cidr" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_IpFilterRule_TenantId_Cidr",
                schema: "identity",
                table: "IpFilterRules");

            migrationBuilder.CreateIndex(
                name: "IX_IpFilterRule_Cidr",
                schema: "identity",
                table: "IpFilterRules",
                column: "Cidr");
        }
    }
}
