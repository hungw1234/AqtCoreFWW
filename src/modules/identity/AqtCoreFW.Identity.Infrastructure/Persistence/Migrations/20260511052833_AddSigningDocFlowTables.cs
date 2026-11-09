using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSigningDocFlowTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SigningDocFlowRecords",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    OriginalStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SignedStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    JwtNonce = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningDocFlowRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SigningDocFlowNonces",
                schema: "identity",
                columns: table => new
                {
                    Nonce = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocFlowRecordId = table.Column<long>(type: "bigint", nullable: false),
                    UseCount = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    MaxUses = table.Column<int>(type: "int", nullable: false, defaultValueSql: "2"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningDocFlowNonces", x => x.Nonce);
                    table.ForeignKey(
                        name: "FK_SigningDocFlowNonces_SigningDocFlowRecords_DocFlowRecordId",
                        column: x => x.DocFlowRecordId,
                        principalSchema: "identity",
                        principalTable: "SigningDocFlowRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowNonce_ExpiresAt",
                schema: "identity",
                table: "SigningDocFlowNonces",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowNonces_DocFlowRecordId",
                schema: "identity",
                table: "SigningDocFlowNonces",
                column: "DocFlowRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowRecord_JwtNonce",
                schema: "identity",
                table: "SigningDocFlowRecords",
                column: "JwtNonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowRecord_Status",
                schema: "identity",
                table: "SigningDocFlowRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowRecord_TenantId_DocId_Status",
                schema: "identity",
                table: "SigningDocFlowRecords",
                columns: new[] { "TenantId", "DocId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SigningDocFlowNonces",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SigningDocFlowRecords",
                schema: "identity");
        }
    }
}
