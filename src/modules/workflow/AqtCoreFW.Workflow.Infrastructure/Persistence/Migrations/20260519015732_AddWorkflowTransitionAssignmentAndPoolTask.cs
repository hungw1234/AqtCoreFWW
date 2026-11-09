using System;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTransitionAssignmentAndPoolTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CandidateRefsJson",
                schema: "workflow",
                table: "WorkflowTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedAt",
                schema: "workflow",
                table: "WorkflowTasks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "workflow",
                table: "WorkflowTasks",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "WorkflowTaskCandidateIndex",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowTaskId = table.Column<long>(type: "bigint", nullable: false),
                    ClauseId = table.Column<int>(type: "int", nullable: false),
                    CombineMode = table.Column<int>(type: "int", nullable: false),
                    PrincipalType = table.Column<int>(type: "int", nullable: false),
                    PrincipalRefId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTaskCandidateIndex", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitionAssignments",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowTransitionId = table.Column<long>(type: "bigint", nullable: false),
                    CombineMode = table.Column<int>(type: "int", nullable: false),
                    AllowAssignorOverride = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitionAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitionAssignmentClauses",
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
                    table.PrimaryKey("PK_WorkflowTransitionAssignmentClauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitionAssignmentClauses_WorkflowTransitionAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowTransitionAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskCandidateIndex_IsDeleted",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskCandidateIndex_TaskId_ClauseId",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex",
                columns: new[] { "WorkflowTaskId", "ClauseId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskCandidateIndex_Tenant_Principal",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex",
                columns: new[] { "TenantId", "PrincipalType", "PrincipalRefId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskCandidateIndex_TenantId_CreatedAt",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskCandidateIndex_TenantId_Id",
                schema: "workflow",
                table: "WorkflowTaskCandidateIndex",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionAssignmentClause_IsDeleted",
                schema: "workflow",
                table: "WorkflowTransitionAssignmentClauses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionAssignmentClauses_AssignmentId",
                schema: "workflow",
                table: "WorkflowTransitionAssignmentClauses",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionAssignment_IsDeleted",
                schema: "workflow",
                table: "WorkflowTransitionAssignments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowTransitionAssignments_TransitionId",
                schema: "workflow",
                table: "WorkflowTransitionAssignments",
                column: "WorkflowTransitionId",
                unique: true);

            // RT2-8: tenant-RLS ONLY on the runtime read-model. WorkflowTransitionAssignment(s)
            // and clauses are workflow-definition config (inherit definition tenancy) — NO RLS.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "workflow", "WorkflowTaskCandidateIndex"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the schemabound RLS policy/function before dropping its table.
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "workflow", "WorkflowTaskCandidateIndex"))
                migrationBuilder.Sql(sql);

            migrationBuilder.DropTable(
                name: "WorkflowTaskCandidateIndex",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowTransitionAssignmentClauses",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowTransitionAssignments",
                schema: "workflow");

            migrationBuilder.DropColumn(
                name: "CandidateRefsJson",
                schema: "workflow",
                table: "WorkflowTasks");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                schema: "workflow",
                table: "WorkflowTasks");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "workflow",
                table: "WorkflowTasks");
        }
    }
}
