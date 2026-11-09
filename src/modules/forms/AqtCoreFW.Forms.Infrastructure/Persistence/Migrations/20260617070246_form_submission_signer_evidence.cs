using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class form_submission_signer_evidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormSubmissionSignerEvidences",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormSubmissionId = table.Column<long>(type: "bigint", nullable: false),
                    SignerRoleKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SignatureVerdict = table.Column<bool>(type: "bit", nullable: false),
                    SignerSubject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SignerSerial = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SignatureVerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SignatureChainEvidence = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignerCertDer = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    SignerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissionSignerEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormSubmissionSignerEvidences_FormSubmissions_FormSubmissionId",
                        column: x => x.FormSubmissionId,
                        principalSchema: "forms",
                        principalTable: "FormSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormTemplateSignerRoles",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    RoleKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleLabelVi = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    PlacementJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplateSignerRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormTemplateSignerRoles_FormTemplates_FormTemplateId",
                        column: x => x.FormTemplateId,
                        principalSchema: "forms",
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionSignerEvidence_IsDeleted",
                schema: "forms",
                table: "FormSubmissionSignerEvidences",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissionSignerEvidences_FormSubmissionId_SignerRoleKey",
                schema: "forms",
                table: "FormSubmissionSignerEvidences",
                columns: new[] { "FormSubmissionId", "SignerRoleKey" });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateSignerRole_IsDeleted",
                schema: "forms",
                table: "FormTemplateSignerRoles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateSignerRoles_FormTemplateId_DisplayOrder",
                schema: "forms",
                table: "FormTemplateSignerRoles",
                columns: new[] { "FormTemplateId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormSubmissionSignerEvidences",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "FormTemplateSignerRoles",
                schema: "forms");
        }
    }
}
