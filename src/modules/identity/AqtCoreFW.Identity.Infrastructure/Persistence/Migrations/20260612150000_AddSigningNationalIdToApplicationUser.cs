using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Add the nullable SigningNationalId column to AspNetUsers — the signer's CCCD used as the
    /// CA-side user_id for remote signing providers (Viettel Mysign). Confidential PII, encrypted at
    /// rest by EncryptedStringConverter at runtime; nvarchar(1024) accommodates the AES-256-GCM
    /// ciphertext (same width as PhoneNumber/TotpSecret). Pure additive column, no data backfill.
    ///
    /// The [DbContext]/[Migration] attributes + BuildTargetModel live in the companion .Designer.cs;
    /// the model snapshot is updated alongside so the migration-consistency guard diffs the right model.
    /// </summary>
    public partial class AddSigningNationalIdToApplicationUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SigningNationalId",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SigningNationalId",
                schema: "identity",
                table: "AspNetUsers");
        }
    }
}
