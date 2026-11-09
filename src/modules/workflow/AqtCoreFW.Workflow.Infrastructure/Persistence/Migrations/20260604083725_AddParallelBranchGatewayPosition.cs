using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParallelBranchGatewayPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PositionX",
                schema: "workflow",
                table: "WorkflowParallelBranches",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionY",
                schema: "workflow",
                table: "WorkflowParallelBranches",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PositionX",
                schema: "workflow",
                table: "WorkflowParallelBranches");

            migrationBuilder.DropColumn(
                name: "PositionY",
                schema: "workflow",
                table: "WorkflowParallelBranches");
        }
    }
}
