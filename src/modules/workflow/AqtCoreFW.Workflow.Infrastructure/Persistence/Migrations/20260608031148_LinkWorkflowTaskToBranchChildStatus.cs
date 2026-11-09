using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkWorkflowTaskToBranchChildStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deploy pre-flight (durable environments only): this FK rejects any WorkflowTask whose
            // BranchChildStatusId is non-null but matches no WorkflowBranchChildStatus.Id — including the
            // stray 0 value the pre-fix fork-staging bug wrote. Such a row only exists if a parallel-branch
            // fork ran before this fix; none have in any durable DB (the fork was provably broken).
            // If a target environment can hold one, NULL the orphan rows before applying:
            //   UPDATE workflow.WorkflowTasks SET BranchChildStatusId = NULL
            //   WHERE BranchChildStatusId IS NOT NULL
            //     AND BranchChildStatusId NOT IN (SELECT Id FROM workflow.WorkflowBranchChildStatuses);
            // The migration is transactional on SQL Server, so a violation aborts cleanly (no data loss).
            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowTasks_WorkflowBranchChildStatuses_BranchChildStatusId",
                schema: "workflow",
                table: "WorkflowTasks",
                column: "BranchChildStatusId",
                principalSchema: "workflow",
                principalTable: "WorkflowBranchChildStatuses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowTasks_WorkflowBranchChildStatuses_BranchChildStatusId",
                schema: "workflow",
                table: "WorkflowTasks");
        }
    }
}
