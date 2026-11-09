using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSigningRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SigningRequests",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtifactKind = table.Column<int>(type: "int", nullable: false),
                    ArtifactRef = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AssignedSignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Placement = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    UnderlyingKind = table.Column<int>(type: "int", nullable: false),
                    UnderlyingRecordId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "AK_SigningRequest_PublicId",
                schema: "identity",
                table: "SigningRequests",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequest_IsDeleted",
                schema: "identity",
                table: "SigningRequests",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequest_TenantId_AssignedSignerId",
                schema: "identity",
                table: "SigningRequests",
                columns: new[] { "TenantId", "AssignedSignerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequest_TenantId_CreatedAt",
                schema: "identity",
                table: "SigningRequests",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequest_TenantId_Id",
                schema: "identity",
                table: "SigningRequests",
                columns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SigningRequests",
                schema: "identity");
        }
    }
}
