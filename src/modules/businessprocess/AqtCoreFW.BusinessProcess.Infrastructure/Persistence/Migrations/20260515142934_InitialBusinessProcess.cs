using System;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "businessprocess");

            migrationBuilder.CreateTable(
                name: "BusinessProcessDefinitions",
                schema: "businessprocess",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WorkflowDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    IntakeFormTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    IntakeFormTemplateVersion = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProcessDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "businessprocess",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SchemaName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessProcessStateForms",
                schema: "businessprocess",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessProcessDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowStateId = table.Column<long>(type: "bigint", nullable: false),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    FormTemplateVersion = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProcessStateForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessProcessStateForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                        column: x => x.BusinessProcessDefinitionId,
                        principalSchema: "businessprocess",
                        principalTable: "BusinessProcessDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessProcessTransitionForms",
                schema: "businessprocess",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessProcessDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowTransitionId = table.Column<long>(type: "bigint", nullable: false),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    FormTemplateVersion = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProcessTransitionForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessProcessTransitionForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                        column: x => x.BusinessProcessDefinitionId,
                        principalSchema: "businessprocess",
                        principalTable: "BusinessProcessDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "AK_BusinessProcessDefinition_PublicId",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessDefinition_IsDeleted",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessDefinition_TenantId_CreatedAt",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessDefinition_TenantId_Id",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessDefinitions_Tenant_IsActive",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessDefinitions_WorkflowDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcessDefinitions_Tenant_Code",
                schema: "businessprocess",
                table: "BusinessProcessDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_BusinessProcessStateForm_PublicId",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessStateForm_IsDeleted",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessStateForm_TenantId_CreatedAt",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessStateForm_TenantId_Id",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessStateForms_Bp_State_DisplayOrder",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                columns: new[] { "BusinessProcessDefinitionId", "WorkflowStateId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcessStateForms_Bp_State_Form",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                columns: new[] { "BusinessProcessDefinitionId", "WorkflowStateId", "FormTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_BusinessProcessTransitionForm_PublicId",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessTransitionForm_IsDeleted",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessTransitionForm_TenantId_CreatedAt",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessTransitionForm_TenantId_Id",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcessTransitionForms_Bp_Transition_DisplayOrder",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                columns: new[] { "BusinessProcessDefinitionId", "WorkflowTransitionId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_BusinessProcessTransitionForms_Bp_Transition_Form",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                columns: new[] { "BusinessProcessDefinitionId", "WorkflowTransitionId", "FormTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_businessprocess_ProcessedAt",
                schema: "businessprocess",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");

            // === BP MVP P3 (Session 1, D3): RLS tenant-isolation policies ===
            // Mirrors Forms/Workflow precedent (RestorePhase06RlsPolicies).
            // Each migrationBuilder.Sql() is one batch — CREATE FUNCTION + CREATE SECURITY POLICY
            // must be batch-isolated (no GO parsing in EF runtime).
            // db_owner bypasses (built into RlsMigrationHelper) for admin migrations.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "businessprocess",
                "BusinessProcessDefinitions",
                "BusinessProcessStateForms",
                "BusinessProcessTransitionForms"))
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop RLS policies BEFORE dropping tables (security policy depends on table).
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "businessprocess",
                "BusinessProcessDefinitions",
                "BusinessProcessStateForms",
                "BusinessProcessTransitionForms"))
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.DropTable(
                name: "BusinessProcessStateForms",
                schema: "businessprocess");

            migrationBuilder.DropTable(
                name: "BusinessProcessTransitionForms",
                schema: "businessprocess");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "businessprocess");

            migrationBuilder.DropTable(
                name: "BusinessProcessDefinitions",
                schema: "businessprocess");
        }
    }
}
