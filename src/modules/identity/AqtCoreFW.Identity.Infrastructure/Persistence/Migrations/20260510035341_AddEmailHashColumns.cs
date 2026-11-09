using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// QĐ742 §4.4 Wave-3 — adds HMAC-SHA-256 email hash columns to AspNetUsers,
    /// and widens Email/NormalizedEmail to nvarchar(1024) for AES-256-GCM ciphertext.
    ///
    /// Schema changes:
    ///   • Email              nvarchar(256) → nvarchar(1024)  — stores AES-256-GCM ciphertext at runtime
    ///   • NormalizedEmail    nvarchar(256) → nvarchar(1024)  — stores AES-256-GCM ciphertext at runtime
    ///   • EmailHash          nvarchar(64)  NULL  — HMAC-SHA-256(email canonical, pepper)
    ///   • NormalizedEmailHash nvarchar(64) NULL  — HMAC-SHA-256(normalizedEmail, pepper)
    ///   • IX_AspNetUsers_NormalizedEmailHash — filtered unique index (WHERE NOT NULL)
    ///
    /// Why widen Email/NormalizedEmail here:
    ///   EncryptedStringConverter is applied at runtime only (design-time ctor has no IKeyResolver).
    ///   AES-256-GCM ciphertext for a 254-char email = ~376 chars Base64 (v{N}: prefix + IV+cipher+tag).
    ///   The default nvarchar(256) cannot hold encrypted email — explicit AlterColumn required.
    ///   This matches the TotpSecret precedent (also widened to nvarchar(1024) in configuration).
    ///
    /// Rows remain NULL until the Wave-4 BackfillEmailHashJob runs post-deploy.
    /// </summary>
    public partial class AddEmailHashColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Widen Email and NormalizedEmail to hold AES-256-GCM ciphertext at runtime.
            // AlterColumn is explicit because design-time migration scaffolding (parameterless ctor)
            // cannot see IKeyResolver, so EF did not emit a schema change for HasMaxLength(1024).
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailHash",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmailHash",
                unique: true,
                filter: "[NormalizedEmailHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailHash",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers");

            // Restore Email and NormalizedEmail to their pre-Wave-3 widths.
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
