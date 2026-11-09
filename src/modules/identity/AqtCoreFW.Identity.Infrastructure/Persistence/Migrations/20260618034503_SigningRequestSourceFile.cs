using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SigningRequestSourceFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceFileId",
                schema: "identity",
                table: "SigningRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequest_TenantId_SourceFileId",
                schema: "identity",
                table: "SigningRequests",
                columns: new[] { "TenantId", "SourceFileId" },
                filter: "[SourceFileId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningRequest_TenantId_SourceFileId",
                schema: "identity",
                table: "SigningRequests");

            migrationBuilder.DropColumn(
                name: "SourceFileId",
                schema: "identity",
                table: "SigningRequests");
        }
    }
}
