using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceDossierIdAndScopeActiveIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WorkflowInstances_Active_Entity",
                schema: "workflow",
                table: "WorkflowInstances");

            migrationBuilder.AddColumn<Guid>(
                name: "DossierId",
                schema: "workflow",
                table: "WorkflowInstances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowInstances_Active_Entity",
                schema: "workflow",
                table: "WorkflowInstances",
                columns: new[] { "EntityType", "EntityId", "TenantId" },
                unique: true,
                filter: "[CompletedAt] IS NULL AND [DossierId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fail-loud guard: restoring the original [CompletedAt] IS NULL filter would throw a raw
            // duplicate-key (SqlException 1505) if any (EntityType, EntityId, TenantId) now has more than
            // one active run — exactly the multi-run capability this migration adds. Detect that first and
            // raise a clear, actionable error instead of a raw 1505; never destroy run data on a downgrade.
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM [workflow].[WorkflowInstances]
    WHERE [CompletedAt] IS NULL
    GROUP BY [EntityType], [EntityId], [TenantId]
    HAVING COUNT(*) > 1
)
    THROW 50000, 'Cannot revert: one or more entities have multiple active workflow instances (BusinessProcess multi-run). Complete or cancel the extra active runs per entity before downgrading, then retry.', 1;");

            migrationBuilder.DropIndex(
                name: "UX_WorkflowInstances_Active_Entity",
                schema: "workflow",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "DossierId",
                schema: "workflow",
                table: "WorkflowInstances");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowInstances_Active_Entity",
                schema: "workflow",
                table: "WorkflowInstances",
                columns: new[] { "EntityType", "EntityId", "TenantId" },
                unique: true,
                filter: "[CompletedAt] IS NULL");
        }
    }
}
