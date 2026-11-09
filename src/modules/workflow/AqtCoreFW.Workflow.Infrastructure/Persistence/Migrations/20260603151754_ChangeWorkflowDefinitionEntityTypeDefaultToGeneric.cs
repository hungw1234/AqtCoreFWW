using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeWorkflowDefinitionEntityTypeDefaultToGeneric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                schema: "workflow",
                table: "WorkflowDefinitions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Generic",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldDefaultValue: "Case");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EntityType",
                schema: "workflow",
                table: "WorkflowDefinitions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Case",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldDefaultValue: "Generic");
        }
    }
}
