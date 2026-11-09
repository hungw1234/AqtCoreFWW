using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Infrastructure.Messaging.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMessagesIdempotencyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_messages",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsumerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DedupKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_processed_messages_ConsumerName_DedupKey",
                schema: "messaging",
                table: "processed_messages",
                columns: new[] { "ConsumerName", "DedupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_messages_ProcessedAtUtc",
                schema: "messaging",
                table: "processed_messages",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "messaging");
        }
    }
}
