using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormSubmissionWorkflowLinkPendingAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormSubmissionWorkflowLinkPendingAttempts",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ApprovalWorkflowDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastFailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissionWorkflowLinkPendingAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWfLinkPendingAttempts_Tenant_Status_NextRetryAt",
                schema: "forms",
                table: "FormSubmissionWorkflowLinkPendingAttempts",
                columns: new[] { "TenantId", "Status", "NextRetryAt" },
                filter: "[NextRetryAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWfLinkPendingAttempts_Tenant_Template_Status",
                schema: "forms",
                table: "FormSubmissionWorkflowLinkPendingAttempts",
                columns: new[] { "TenantId", "FormTemplateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWorkflowLinkPendingAttempt_IsDeleted",
                schema: "forms",
                table: "FormSubmissionWorkflowLinkPendingAttempts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWorkflowLinkPendingAttempt_TenantId_CreatedAt",
                schema: "forms",
                table: "FormSubmissionWorkflowLinkPendingAttempts",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionWorkflowLinkPendingAttempt_TenantId_Id",
                schema: "forms",
                table: "FormSubmissionWorkflowLinkPendingAttempts",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_FormSubmissionWfLinkPendingAttempts_Submission_Tenant",
                schema: "forms",
                table: "FormSubmissionWorkflowLinkPendingAttempts",
                columns: new[] { "SubmissionId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormSubmissionWorkflowLinkPendingAttempts",
                schema: "forms");
        }
    }
}
