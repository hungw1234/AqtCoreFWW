using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassificationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassificationRules",
                schema: "audit",
                columns: table => new
                {
                    Level = table.Column<int>(type: "int", nullable: false),
                    MustEncryptAtRest = table.Column<bool>(type: "bit", nullable: false),
                    AllowExport = table.Column<bool>(type: "bit", nullable: false),
                    MaxRetentionDays = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationRules", x => x.Level);
                });

            migrationBuilder.InsertData(
                schema: "audit",
                table: "ClassificationRules",
                columns: new[] { "Level", "AllowExport", "MaxRetentionDays", "MustEncryptAtRest", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 0, true, null, false, new DateTime(2026, 5, 10, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { 1, true, 365, false, new DateTime(2026, 5, 10, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { 2, false, 1825, true, new DateTime(2026, 5, 10, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { 3, false, 365, true, new DateTime(2026, 5, 10, 0, 0, 0, 0, DateTimeKind.Utc), "system" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassificationRules",
                schema: "audit");
        }
    }
}
