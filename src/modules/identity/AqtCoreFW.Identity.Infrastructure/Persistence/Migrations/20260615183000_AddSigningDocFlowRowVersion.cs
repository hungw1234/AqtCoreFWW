using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds an optimistic-concurrency rowversion to SigningDocFlowNonces + SigningDocFlowRecords so
    /// nonce consumption and the doc-flow status transitions are atomic against concurrent callers.
    ///
    /// Before this, <c>SigningDocFlowNonce.TryUse()</c> was an in-memory UseCount increment with no DB
    /// guard: two parallel uploads carrying the same JWT both read UseCount, both passed the gate, and
    /// both persisted — a replay / double-spend. The rowversion column makes the first SaveChanges win
    /// and the losers fail with DbUpdateConcurrencyException (the handler maps that to 401). A CHECK
    /// constraint on the nonce enforces UseCount &lt;= MaxUses at the storage engine as defence in depth.
    ///
    /// SQL Server auto-populates rowversion for existing rows, so this is additive with no backfill.
    /// The [DbContext]/[Migration] attributes + BuildTargetModel live in the companion .Designer.cs;
    /// the model snapshot is updated alongside so the migration-consistency guard diffs the right model.
    /// </summary>
    public partial class AddSigningDocFlowRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "identity",
                table: "SigningDocFlowNonces",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "identity",
                table: "SigningDocFlowRecords",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Defence in depth: the storage engine refuses to let UseCount exceed MaxUses even if a
            // future code path were to bypass the optimistic-concurrency token above.
            migrationBuilder.Sql(
                "ALTER TABLE [identity].[SigningDocFlowNonces] " +
                "ADD CONSTRAINT [CK_SigningDocFlowNonces_UseCount_LeMaxUses] CHECK ([UseCount] <= [MaxUses]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [identity].[SigningDocFlowNonces] " +
                "DROP CONSTRAINT IF EXISTS [CK_SigningDocFlowNonces_UseCount_LeMaxUses];");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "identity",
                table: "SigningDocFlowRecords");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "identity",
                table: "SigningDocFlowNonces");
        }
    }
}
