using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only backfill: materializes WorkflowTaskCandidateIndex rows for open pool tasks
    /// (Status IN (Open=0, InProgress=1), AssigneeId IS NULL) that were created before the
    /// WorkflowTaskFactory producer fix landed. These tasks had CandidateRefsJson populated
    /// but zero index rows — rendering them invisible in all inbox and claim queries.
    ///
    /// SQL body sourced from <see cref="BackfillWorkflowTaskCandidateIndexSql"/> (shared with
    /// the migration integration test — DRY, no drift). Down is a no-op (forward-only data fix).
    /// </summary>
    public partial class BackfillWorkflowTaskCandidateIndexFromSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in BackfillWorkflowTaskCandidateIndexSql.UpStatements)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in BackfillWorkflowTaskCandidateIndexSql.DownStatements)
                migrationBuilder.Sql(sql);
        }
    }
}
