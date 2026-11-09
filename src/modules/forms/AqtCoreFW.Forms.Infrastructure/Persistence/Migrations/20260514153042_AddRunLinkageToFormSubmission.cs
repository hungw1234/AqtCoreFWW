using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunLinkageToFormSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WorkflowStateId",
                schema: "forms",
                table: "FormSubmissions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_LatestStateForm",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "WorkflowInstanceId", "WorkflowStateId", "FormTemplateId", "SubmittedAt" },
                filter: "[WorkflowInstanceId] IS NOT NULL AND [WorkflowStateId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Id", "Status", "SubmittedBy" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_LatestStateForm",
                schema: "forms",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "WorkflowStateId",
                schema: "forms",
                table: "FormSubmissions");
        }
    }
}
