using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTaskCandidateIndexForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowTaskCandidateIndex_WorkflowTasks_WorkflowTaskId",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex",
                column: "WorkflowTaskId",
                principalSchema: "workflow",
                principalTable: "WorkflowTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowTaskCandidateIndex_WorkflowTasks_WorkflowTaskId",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex");
        }
    }
}
