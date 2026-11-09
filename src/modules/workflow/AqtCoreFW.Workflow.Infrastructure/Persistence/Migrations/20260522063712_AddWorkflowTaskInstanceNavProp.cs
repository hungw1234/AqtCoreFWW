using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTaskInstanceNavProp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowTasks_WorkflowInstances_WorkflowInstanceId",
                schema: "workflow",
                table: "WorkflowTasks",
                column: "WorkflowInstanceId",
                principalSchema: "workflow",
                principalTable: "WorkflowInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowTasks_WorkflowInstances_WorkflowInstanceId",
                schema: "workflow",
                table: "WorkflowTasks");
        }
    }
}
