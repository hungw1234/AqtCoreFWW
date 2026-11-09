using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class signature_session : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords");

            migrationBuilder.CreateTable(
                name: "DocumentSignatureSessions",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BpInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ArtifactRef = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Threshold = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSignatureSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSignatureSessionSigners",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false, defaultValueSql: "1"),
                    State = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    SigningRequestPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSignatureSessionSigners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSignatureSessionSigners_DocumentSignatureSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "identity",
                        principalTable: "DocumentSignatureSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords",
                columns: new[] { "TenantId", "DocId", "SignerSequenceNumber" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "AK_DocumentSignatureSession_PublicId",
                schema: "identity",
                table: "DocumentSignatureSessions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatureSession_IsDeleted",
                schema: "identity",
                table: "DocumentSignatureSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatureSession_TenantId_CreatedAt",
                schema: "identity",
                table: "DocumentSignatureSessions",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatureSession_TenantId_Id",
                schema: "identity",
                table: "DocumentSignatureSessions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_DocumentSignatureSession_TenantId_BpInstanceId_StepKey",
                schema: "identity",
                table: "DocumentSignatureSessions",
                columns: new[] { "TenantId", "BpInstanceId", "StepKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatureSessionSigner_TenantId_SessionId_Sequence",
                schema: "identity",
                table: "DocumentSignatureSessionSigners",
                columns: new[] { "TenantId", "SessionId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_DocumentSignatureSessionSigner_SessionId_SignerId",
                schema: "identity",
                table: "DocumentSignatureSessionSigners",
                columns: new[] { "SessionId", "SignerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSignatureSessionSigners",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "DocumentSignatureSessions",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "UX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords");

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords",
                columns: new[] { "TenantId", "DocId", "SignerSequenceNumber" });
        }
    }
}
