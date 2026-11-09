using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowBranchChildAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowBranchChildAssignments",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchChildId = table.Column<long>(type: "bigint", nullable: false),
                    CombineMode = table.Column<int>(type: "int", nullable: false),
                    AllowAssignorOverride = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBranchChildAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowBranchChildAssignments_WorkflowBranchChildren_BranchChildId",
                        column: x => x.BranchChildId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowBranchChildren",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowBranchChildAssignmentClauses",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    RefsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBranchChildAssignmentClauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowBranchChildAssignmentClauses_WorkflowBranchChildAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowBranchChildAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChildAssignmentClause_IsDeleted",
                schema: "workflow",
                table: "WorkflowBranchChildAssignmentClauses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChildAssignmentClauses_AssignmentId",
                schema: "workflow",
                table: "WorkflowBranchChildAssignmentClauses",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChildAssignment_IsDeleted",
                schema: "workflow",
                table: "WorkflowBranchChildAssignments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowBranchChildAssignments_BranchChildId",
                schema: "workflow",
                table: "WorkflowBranchChildAssignments",
                column: "BranchChildId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowBranchChildAssignmentClauses",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowBranchChildAssignments",
                schema: "workflow");
        }
    }
}
