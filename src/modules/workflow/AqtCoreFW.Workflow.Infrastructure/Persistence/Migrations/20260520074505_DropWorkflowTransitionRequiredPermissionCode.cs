using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PHR-V2 Phase 3 Slice 8b (M2) — IRREVERSIBLE drop of legacy
    /// <c>WorkflowTransition.RequiredPermissionCode</c>. Assignment-based authz
    /// (<c>WorkflowTransitionAssignment</c>) has been the sole authz path since Slice 5;
    /// the wire DTO was stripped in Slice 8a. Original (TransitionId, RequiredPermissionCode)
    /// pairs were snapshotted in Slice 2 into
    /// <c>[workflow].[WorkflowTransitionRequiredPermissionCodeBackup]</c> — that table is the
    /// sole recovery source. Down() refuses by design (plan ruling RT2-16, KISS — no
    /// auto-restore at MVP).
    /// </summary>
    public partial class DropWorkflowTransitionRequiredPermissionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredPermissionCode",
                schema: "workflow",
                table: "WorkflowTransitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "S8b column-drop of WorkflowTransition.RequiredPermissionCode is IRREVERSIBLE. " +
                "Backup data preserved in [workflow].[WorkflowTransitionRequiredPermissionCodeBackup] " +
                "(populated by migration 20260519102348_RequiredPermissionCodeToAssignmentPrep). " +
                "Manual recovery only: " +
                "(1) ALTER TABLE [workflow].[WorkflowTransitions] ADD [RequiredPermissionCode] nvarchar(100) NULL; " +
                "(2) UPDATE WT SET RequiredPermissionCode = B.RequiredPermissionCode " +
                "FROM [workflow].[WorkflowTransitions] WT JOIN " +
                "[workflow].[WorkflowTransitionRequiredPermissionCodeBackup] B ON B.WorkflowTransitionId = WT.Id; " +
                "(3) optionally apply S2 Down to clear assignments + backup. " +
                "See plans/260519-0421-process-handler-resolution-v2/phase-03... §Slice 8b Risk Summary.");
        }
    }
}
