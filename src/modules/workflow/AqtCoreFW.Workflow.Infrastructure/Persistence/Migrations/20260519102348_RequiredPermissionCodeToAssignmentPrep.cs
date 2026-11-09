using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Plan 260519-0421 v2, Phase 3 — SESSION 2 (additive RPC→assignment prep, P2b-style).
    /// Pure data migration (no model/schema entity change) — body sourced from
    /// <see cref="RequiredPermissionCodeToAssignmentSql"/> (single source, shared with the
    /// Testcontainers integration test). RequiredPermissionCode column + readers stay LIVE
    /// → pre-P3 behavior byte-identical; the column-drop cutover is a later P3 session.
    /// </summary>
    public partial class RequiredPermissionCodeToAssignmentPrep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RequiredPermissionCodeToAssignmentSql.UpStatements)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RequiredPermissionCodeToAssignmentSql.DownStatements)
                migrationBuilder.Sql(sql);
        }
    }
}
