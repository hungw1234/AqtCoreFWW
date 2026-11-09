using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSignatureConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantSignatureConfig",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ApiCredentialsRef = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AllowedCaRootThumbprints = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false, defaultValue: ""),
                    OcspMode = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSignatureConfig", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSignatureConfig_IsDeleted",
                schema: "identity",
                table: "TenantSignatureConfig",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSignatureConfig_TenantId_CreatedAt",
                schema: "identity",
                table: "TenantSignatureConfig",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSignatureConfig_TenantId_Id",
                schema: "identity",
                table: "TenantSignatureConfig",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UQ_TenantSignatureConfig_TenantId",
                schema: "identity",
                table: "TenantSignatureConfig",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSignatureConfig",
                schema: "identity");
        }
    }
}
