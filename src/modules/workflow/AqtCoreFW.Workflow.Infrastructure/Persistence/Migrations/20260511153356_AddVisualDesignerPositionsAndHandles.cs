using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualDesignerPositionsAndHandles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceHandle",
                schema: "workflow",
                table: "WorkflowTransitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetHandle",
                schema: "workflow",
                table: "WorkflowTransitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionX",
                schema: "workflow",
                table: "WorkflowStates",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionY",
                schema: "workflow",
                table: "WorkflowStates",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceHandle",
                schema: "workflow",
                table: "WorkflowTransitions");

            migrationBuilder.DropColumn(
                name: "TargetHandle",
                schema: "workflow",
                table: "WorkflowTransitions");

            migrationBuilder.DropColumn(
                name: "PositionX",
                schema: "workflow",
                table: "WorkflowStates");

            migrationBuilder.DropColumn(
                name: "PositionY",
                schema: "workflow",
                table: "WorkflowStates");
        }
    }
}
