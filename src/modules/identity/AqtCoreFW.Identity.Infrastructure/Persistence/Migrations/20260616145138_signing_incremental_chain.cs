using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class signing_incremental_chain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SignerSequenceNumber",
                schema: "identity",
                table: "SigningRequests",
                type: "int",
                nullable: false,
                defaultValueSql: "1");

            migrationBuilder.AddColumn<string>(
                name: "PreviousSignedStorageKey",
                schema: "identity",
                table: "SigningDocFlowRecords",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignerSequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords",
                type: "int",
                nullable: false,
                defaultValueSql: "1");

            migrationBuilder.CreateIndex(
                name: "IX_SigningRequest_TenantId_ArtifactRef_SequenceNumber",
                schema: "identity",
                table: "SigningRequests",
                columns: new[] { "TenantId", "ArtifactRef", "SignerSequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords",
                columns: new[] { "TenantId", "DocId", "SignerSequenceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningRequest_TenantId_ArtifactRef_SequenceNumber",
                schema: "identity",
                table: "SigningRequests");

            migrationBuilder.DropIndex(
                name: "IX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords");

            migrationBuilder.DropColumn(
                name: "SignerSequenceNumber",
                schema: "identity",
                table: "SigningRequests");

            migrationBuilder.DropColumn(
                name: "PreviousSignedStorageKey",
                schema: "identity",
                table: "SigningDocFlowRecords");

            migrationBuilder.DropColumn(
                name: "SignerSequenceNumber",
                schema: "identity",
                table: "SigningDocFlowRecords");
        }
    }
}
