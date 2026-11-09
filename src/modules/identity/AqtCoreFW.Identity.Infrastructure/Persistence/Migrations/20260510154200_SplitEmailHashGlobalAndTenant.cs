using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// QĐ742 §4.4 Fixup P0-1 — two-column email hash split (Strategy C).
    ///
    /// Schema changes:
    ///   • Rename EmailHash          → EmailHashGlobal         (Guid.Empty pepper, login path)
    ///   • Rename NormalizedEmailHash → NormalizedEmailHashGlobal (Guid.Empty pepper, login path)
    ///   • Drop IX_AspNetUsers_NormalizedEmailHash (references old column name)
    ///   • Recreate unique filtered index as IX_AspNetUsers_NormalizedEmailHashGlobal
    ///   • Add EmailHashTenant          nvarchar(64) NULL  (per-tenant pepper, future admin queries)
    ///   • Add NormalizedEmailHashTenant nvarchar(64) NULL  (per-tenant pepper)
    ///   • Add IX_AspNetUsers_NormalizedEmailHashTenant (non-unique, filtered WHERE IS NOT NULL)
    ///   • Widen PhoneNumber 256→1024 (Fixup P1-1 PhoneNumber encryption via EncryptedStringConverter)
    ///
    /// Why this order in Up():
    ///   1. DropIndex — index references NormalizedEmailHash by column name; must drop before rename
    ///   2. RenameColumn ×2 — preserves existing hash values (no data loss)
    ///   3. AddColumn ×2 — new nullable tenant columns (NULL until next interceptor save / backfill)
    ///   4. CreateIndex ×2 — recreate global unique + new non-unique tenant index
    ///   5. AlterColumn PhoneNumber — widen nvarchar(256)→nvarchar(1024) for ciphertext headroom
    ///
    /// Existing hash values in EmailHashGlobal/NormalizedEmailHashGlobal after rename:
    ///   Values were written with the OLD interceptor (per-tenant pepper). If TenantId was non-null,
    ///   these values are WRONG for the login path. EmailHashBackfillService WHERE NormalizedEmailHashGlobal IS NULL
    ///   will NOT reprocess them (they are non-null after rename).
    ///   MITIGATION: The rename preserves data but marks it as "global" even though it was tenanted.
    ///   In production this is safe because dev DB is a template (aqtcorefw-template-dev-only.md).
    ///   Downstream projects MUST run the full backfill against fresh data.
    ///   Alternative fixup: run a one-time SQL UPDATE NormalizedEmailHashGlobal = NULL
    ///   to force re-backfill for affected tenanted users.
    /// </summary>
    public partial class SplitEmailHashGlobalAndTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Drop old filtered unique index — references NormalizedEmailHash by name
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers");

            // Step 2: Rename existing hash columns (preserves data, only column name changes)
            migrationBuilder.RenameColumn(
                name: "EmailHash",
                schema: "identity",
                table: "AspNetUsers",
                newName: "EmailHashGlobal");

            migrationBuilder.RenameColumn(
                name: "NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers",
                newName: "NormalizedEmailHashGlobal");

            // Step 3: Add new per-tenant hash columns (nullable — populated by interceptor / backfill)
            migrationBuilder.AddColumn<string>(
                name: "EmailHashTenant",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmailHashTenant",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Step 4a: Recreate unique filtered index on renamed global column
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_NormalizedEmailHashGlobal",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmailHashGlobal",
                unique: true,
                filter: "[NormalizedEmailHashGlobal] IS NOT NULL");

            // Step 4b: Non-unique tenant index (different tenants can share same email)
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_NormalizedEmailHashTenant",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmailHashTenant",
                unique: false,
                filter: "[NormalizedEmailHashTenant] IS NOT NULL");

            // Step 5: Widen PhoneNumber for AES-256-GCM ciphertext (Fixup P1-1)
            // Max ciphertext for a 15-char phone: ~88 chars (v{N}:prefix + Base64[IV+cipher+tag]).
            // nvarchar(1024) matches Email/TotpSecret headroom convention.
            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse PhoneNumber widen
            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                schema: "identity",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            // Drop new tenant index + column
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_NormalizedEmailHashTenant",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_NormalizedEmailHashGlobal",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailHashTenant",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedEmailHashTenant",
                schema: "identity",
                table: "AspNetUsers");

            // Rename global columns back to original names
            migrationBuilder.RenameColumn(
                name: "NormalizedEmailHashGlobal",
                schema: "identity",
                table: "AspNetUsers",
                newName: "NormalizedEmailHash");

            migrationBuilder.RenameColumn(
                name: "EmailHashGlobal",
                schema: "identity",
                table: "AspNetUsers",
                newName: "EmailHash");

            // Recreate original unique index on restored name
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_NormalizedEmailHash",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmailHash",
                unique: true,
                filter: "[NormalizedEmailHash] IS NOT NULL");
        }
    }
}
