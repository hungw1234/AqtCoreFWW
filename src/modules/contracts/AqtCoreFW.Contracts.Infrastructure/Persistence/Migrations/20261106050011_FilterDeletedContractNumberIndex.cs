using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Contracts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilterDeletedContractNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_TenantId_ContractNumber",
                schema: "contracts",
                table: "Contracts");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_ContractNumber",
                schema: "contracts",
                table: "Contracts",
                columns: new[] { "TenantId", "ContractNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contracts_TenantId_ContractNumber",
                schema: "contracts",
                table: "Contracts");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_ContractNumber",
                schema: "contracts",
                table: "Contracts",
                columns: new[] { "TenantId", "ContractNumber" },
                unique: true);
        }
    }
}
