using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Re-scope the two ExternalIdentity unique indexes to non-deleted rows
    /// (<c>WHERE [IsDeleted] = 0</c>). Without the filter a soft-deleted (unlinked) link keeps
    /// occupying the unique slot, so re-linking the same provider / external account after an
    /// unlink fails with a duplicate-key error. The partial filter lets one ACTIVE row coexist
    /// with any number of soft-deleted rows for the same key, while still forbidding two active
    /// rows. Mirrors the existing IpFilterRule partial-unique precedent.
    ///
    /// The [DbContext]/[Migration] attributes + BuildTargetModel live in the companion
    /// .Designer.cs (EF convention); the model snapshot is updated alongside so the migration-
    /// consistency guard and future scaffolding diff against the correct model.
    /// </summary>
    public partial class FilterExternalIdentityUniqueIndexesOnSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_UserId_Provider",
                schema: "identity",
                table: "ExternalIdentities");

            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_Provider_ExternalId",
                schema: "identity",
                table: "ExternalIdentities");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_UserId_Provider",
                schema: "identity",
                table: "ExternalIdentities",
                columns: new[] { "UserId", "Provider" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_ExternalId",
                schema: "identity",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "ExternalId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_UserId_Provider",
                schema: "identity",
                table: "ExternalIdentities");

            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_Provider_ExternalId",
                schema: "identity",
                table: "ExternalIdentities");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_UserId_Provider",
                schema: "identity",
                table: "ExternalIdentities",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_ExternalId",
                schema: "identity",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);
        }
    }
}
