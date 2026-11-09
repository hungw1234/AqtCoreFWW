using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Schema-level deployment marker for the cipher version-prefix data migration (D10.2).
    ///
    /// WHY EMPTY: SQL Server has no AES-256-GCM UDF support. The actual row-by-row
    /// re-encryption (legacy cipher → v1:cipher) is performed by <c>LegacyCipherUpgrader</c>
    /// invoked post-EF-Migrate in host startup. This migration entry serves as the deployment
    /// anchor so Migrate() is idempotent across environments.
    ///
    /// DATA MIGRATED: [identity].[AspNetUsers].[TotpSecret]
    ///   WHERE TotpSecret IS NOT NULL AND TotpSecret NOT LIKE 'v[0-9]%:%'
    ///
    /// Runbook: docs/ops/vault-key-rotation-runbook.md
    /// Down: strips v{N}: prefix from all rows (only safe before any v2+ writes).
    /// </summary>
    public partial class AddCipherVersionPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema changes — data migration performed by LegacyCipherUpgrader at host startup.
            // See: src/shared/AqtCoreFW.Infrastructure/Persistence/LegacyCipherUpgrader.cs
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down intentionally left empty.
            // To reverse: run LegacyCipherUpgrader with strip-prefix mode (future ops task).
            // WARNING: reversing is only safe before any v2+ keys are written.
        }
    }
}
