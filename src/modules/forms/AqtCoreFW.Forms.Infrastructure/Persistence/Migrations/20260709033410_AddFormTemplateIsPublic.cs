using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormTemplateIsPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                schema: "forms",
                table: "FormTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Behavior-preserving backfill: every currently-Active, non-deleted template is
            // already reachable through the anonymous public endpoints today (they ignore IsPublic),
            // so mark them public to avoid any existing form 404-ing once the RLS carve-out requires
            // IsPublic=1. New templates keep the DB default (0) and are private until explicitly opted in.
            migrationBuilder.Sql(
                "UPDATE [forms].[FormTemplates] SET IsPublic = 1 WHERE Status = 'Active' AND IsDeleted = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                schema: "forms",
                table: "FormTemplates");
        }
    }
}
