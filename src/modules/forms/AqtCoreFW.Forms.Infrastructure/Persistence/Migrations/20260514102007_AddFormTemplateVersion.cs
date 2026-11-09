using System;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormTemplateVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormTemplateVersions",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_FormTemplates_FormTemplateId",
                        column: x => x.FormTemplateId,
                        principalSchema: "forms",
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersion_IsDeleted",
                schema: "forms",
                table: "FormTemplateVersions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersion_Tenant_Template_Version",
                schema: "forms",
                table: "FormTemplateVersions",
                columns: new[] { "TenantId", "FormTemplateId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersion_TenantId_CreatedAt",
                schema: "forms",
                table: "FormTemplateVersions",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersion_TenantId_Id",
                schema: "forms",
                table: "FormTemplateVersions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplateVersion_Template_Version",
                schema: "forms",
                table: "FormTemplateVersions",
                columns: new[] { "FormTemplateId", "VersionNumber" },
                unique: true);

            // Phase-06 RLS parity — tenant isolation enforced at DB engine for snapshots too.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "forms", "FormTemplateVersions"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "forms", "FormTemplateVersions"))
                migrationBuilder.Sql(sql);

            migrationBuilder.DropTable(
                name: "FormTemplateVersions",
                schema: "forms");
        }
    }
}
