using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceHistoryTransitionedByIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceHistories_TransitionedBy_InstanceId",
                schema: "workflow",
                table: "WorkflowInstanceHistories",
                columns: new[] { "TransitionedBy", "InstanceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstanceHistories_TransitionedBy_InstanceId",
                schema: "workflow",
                table: "WorkflowInstanceHistories");
        }
    }
}
