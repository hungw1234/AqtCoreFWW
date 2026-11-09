using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMysignCallbackTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SigningPendingTransactions",
                schema: "identity",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InitiatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    Signature = table.Column<byte[]>(type: "varbinary(8000)", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningPendingTransactions", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "MysignInboundTokens",
                schema: "identity",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MysignInboundTokens", x => x.TokenHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SigningPendingTransaction_TenantId_UserId",
                schema: "identity",
                table: "SigningPendingTransactions",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SigningPendingTransaction_Status_ExpiresAt",
                schema: "identity",
                table: "SigningPendingTransactions",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MysignInboundToken_ExpiresAt",
                schema: "identity",
                table: "MysignInboundTokens",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SigningPendingTransactions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "MysignInboundTokens",
                schema: "identity");
        }
    }
}
