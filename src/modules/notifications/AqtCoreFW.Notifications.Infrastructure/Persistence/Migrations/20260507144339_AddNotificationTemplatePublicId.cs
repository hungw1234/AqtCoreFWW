using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplatePublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                schema: "notifications",
                table: "NotificationTemplates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing rows with NEWID() — dev-only template repo (no prod data)
            migrationBuilder.Sql(
                "UPDATE [notifications].[NotificationTemplates] SET [PublicId] = NEWID() WHERE [PublicId] = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "AK_NotificationTemplate_PublicId",
                schema: "notifications",
                table: "NotificationTemplates",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_NotificationTemplate_PublicId",
                schema: "notifications",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "PublicId",
                schema: "notifications",
                table: "NotificationTemplates");
        }
    }
}
