using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptRtbfPiiColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // QĐ 742 §4.4 Wave-4: widen SubjectEmail and CitizenNationalId to accommodate
            // AES-256-GCM ciphertext (v{N}: prefix + Base64[nonce:12 + tag:16 + ciphertext]).
            //
            // SubjectEmail: nvarchar(256) → nvarchar(1024)
            //   A 254-char email produces ~376-char ciphertext; nvarchar(256) overflows.
            //   nvarchar(1024) gives headroom for long emails and future algorithm changes.
            //
            // CitizenNationalId: nvarchar(12) → nvarchar(256)
            //   A 12-char national ID produces ~76-char ciphertext; nvarchar(12) is far too small.
            //   nvarchar(256) gives headroom above the 76-char minimum.
            //
            // PlaintextToEncryptedUpgrader (Program.cs startup hook) encrypts existing plaintext
            // rows BEFORE this migration is first read by the application. Column widening here
            // is schema-only; the data migration runs outside EF migrations (CLR required for AES-GCM).

            migrationBuilder.AlterColumn<string>(
                name: "SubjectEmail",
                schema: "audit",
                table: "RtbfRequests",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CitizenNationalId",
                schema: "audit",
                table: "RtbfRequests",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(12)",
                oldMaxLength: 12,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore original column widths (pre-encryption schema).
            // WARNING: any encrypted rows will be truncated if data is wider than the old column.
            // Run PlaintextToEncryptedUpgrader cleanup (decrypt + restore plaintext) before Down.

            migrationBuilder.AlterColumn<string>(
                name: "SubjectEmail",
                schema: "audit",
                table: "RtbfRequests",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CitizenNationalId",
                schema: "audit",
                table: "RtbfRequests",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
