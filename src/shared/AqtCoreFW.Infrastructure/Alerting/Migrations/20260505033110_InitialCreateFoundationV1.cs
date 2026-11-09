using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Infrastructure.Alerting.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateFoundationV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "alerting");

            migrationBuilder.CreateTable(
                name: "AlertRules",
                schema: "alerting",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MetricName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Threshold = table.Column<double>(type: "float", nullable: false),
                    WindowMinutes = table.Column<int>(type: "int", nullable: false),
                    NotifyChannel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NotifyTarget = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsecutiveBreaches = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRule_IsDeleted",
                schema: "alerting",
                table: "AlertRules",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Enabled",
                schema: "alerting",
                table: "AlertRules",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_MetricName",
                schema: "alerting",
                table: "AlertRules",
                column: "MetricName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRules",
                schema: "alerting");
        }
    }
}
