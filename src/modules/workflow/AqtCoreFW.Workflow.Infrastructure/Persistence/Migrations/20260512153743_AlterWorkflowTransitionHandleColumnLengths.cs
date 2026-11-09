using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlterWorkflowTransitionHandleColumnLengths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ALTER COLUMN to a shorter nvarchar errors (does not silently truncate) if any
            // existing value exceeds 16 chars. Safe here: SourceHandle/TargetHandle only ever
            // hold the fixed visual-designer anchor codes ("t-out"|"r-in"|… , ≤8 chars).
            migrationBuilder.AlterColumn<string>(
                name: "TargetHandle",
                schema: "workflow",
                table: "WorkflowTransitions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceHandle",
                schema: "workflow",
                table: "WorkflowTransitions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TargetHandle",
                schema: "workflow",
                table: "WorkflowTransitions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceHandle",
                schema: "workflow",
                table: "WorkflowTransitions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);
        }
    }
}
