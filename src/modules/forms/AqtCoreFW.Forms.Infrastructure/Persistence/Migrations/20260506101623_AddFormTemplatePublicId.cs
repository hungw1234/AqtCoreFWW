using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormTemplatePublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable first so we can backfill existing rows before applying NOT NULL + UNIQUE
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                schema: "forms",
                table: "FormTemplates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing rows with NEWID() — dev-only template repo (no prod data)
            migrationBuilder.Sql(
                "UPDATE forms.FormTemplates SET PublicId = NEWID() WHERE PublicId = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "AK_FormTemplate_PublicId",
                schema: "forms",
                table: "FormTemplates",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_FormTemplate_PublicId",
                schema: "forms",
                table: "FormTemplates");

            migrationBuilder.DropColumn(
                name: "PublicId",
                schema: "forms",
                table: "FormTemplates");
        }
    }
}
