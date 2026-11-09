using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateFoundationV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forms");

            migrationBuilder.CreateTable(
                name: "FormSubmissions",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    FormTemplateVersion = table.Column<int>(type: "int", nullable: false),
                    SubmissionData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "bit", nullable: false),
                    ConsentGiven = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ConsentRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormTemplates",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameVi = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StorageMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    RequiresConsent = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ConsentText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApprovalWorkflowDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "forms",
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
                name: "ViewDefinitions",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultSortField = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DefaultSortDir = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    PageSize = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormFields",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ParentFieldId = table.Column<long>(type: "bigint", nullable: true),
                    FieldKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LabelVi = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AddedInVersion = table.Column<int>(type: "int", nullable: false),
                    ValidationRulesJson = table.Column<string>(type: "nvarchar(2000)", nullable: true),
                    DataSourceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FormulaJson = table.Column<string>(type: "nvarchar(2000)", nullable: true),
                    IsPii = table.Column<bool>(type: "bit", nullable: false),
                    VisibilityRulesJson = table.Column<string>(type: "nvarchar(2000)", nullable: true),
                    RequiredIfJson = table.Column<string>(type: "nvarchar(2000)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormFields_FormTemplates_FormTemplateId",
                        column: x => x.FormTemplateId,
                        principalSchema: "forms",
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ViewColumns",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ViewDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Sortable = table.Column<bool>(type: "bit", nullable: false),
                    Filterable = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Formatter = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViewColumns_ViewDefinitions_ViewDefinitionId",
                        column: x => x.ViewDefinitionId,
                        principalSchema: "forms",
                        principalTable: "ViewDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ViewFilters",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ViewDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViewFilters_ViewDefinitions_ViewDefinitionId",
                        column: x => x.ViewDefinitionId,
                        principalSchema: "forms",
                        principalTable: "ViewDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormFieldOptions",
                schema: "forms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormFieldId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LabelVi = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormFieldOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormFieldOptions_FormFields_FormFieldId",
                        column: x => x.FormFieldId,
                        principalSchema: "forms",
                        principalTable: "FormFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormFieldOption_IsDeleted",
                schema: "forms",
                table: "FormFieldOptions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormFieldOptions_FormFieldId_DisplayOrder",
                schema: "forms",
                table: "FormFieldOptions",
                columns: new[] { "FormFieldId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FormField_IsDeleted",
                schema: "forms",
                table: "FormFields",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormFields_FormTemplateId_FieldKey",
                schema: "forms",
                table: "FormFields",
                columns: new[] { "FormTemplateId", "FieldKey" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "AK_FormSubmission_PublicId",
                schema: "forms",
                table: "FormSubmissions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmission_IsDeleted",
                schema: "forms",
                table: "FormSubmissions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmission_TenantId_CreatedAt",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmission_TenantId_Id",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_TenantId_FormTemplateId",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "TenantId", "FormTemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_TenantId_Status",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_TenantId_SubmittedAt",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "TenantId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_TenantId_SubmittedBy",
                schema: "forms",
                table: "FormSubmissions",
                columns: new[] { "TenantId", "SubmittedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_WorkflowInstanceId",
                schema: "forms",
                table: "FormSubmissions",
                column: "WorkflowInstanceId",
                filter: "[WorkflowInstanceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplate_IsDeleted",
                schema: "forms",
                table: "FormTemplates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplate_TenantId_CreatedAt",
                schema: "forms",
                table: "FormTemplates",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplate_TenantId_Id",
                schema: "forms",
                table: "FormTemplates",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_TenantId_Code",
                schema: "forms",
                table: "FormTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_TenantId_Status",
                schema: "forms",
                table: "FormTemplates",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_forms_ProcessedAt",
                schema: "forms",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ViewColumn_IsDeleted",
                schema: "forms",
                table: "ViewColumns",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ViewColumns_ViewDefinitionId_DisplayOrder",
                schema: "forms",
                table: "ViewColumns",
                columns: new[] { "ViewDefinitionId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ViewDefinition_IsDeleted",
                schema: "forms",
                table: "ViewDefinitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ViewDefinition_TenantId_CreatedAt",
                schema: "forms",
                table: "ViewDefinitions",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ViewDefinition_TenantId_Id",
                schema: "forms",
                table: "ViewDefinitions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ViewDefinitions_TenantId_EntityType",
                schema: "forms",
                table: "ViewDefinitions",
                columns: new[] { "TenantId", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_ViewDefinitions_TenantId_IsDefault",
                schema: "forms",
                table: "ViewDefinitions",
                columns: new[] { "TenantId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_ViewFilter_IsDeleted",
                schema: "forms",
                table: "ViewFilters",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ViewFilters_ViewDefinitionId",
                schema: "forms",
                table: "ViewFilters",
                column: "ViewDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormFieldOptions",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "FormSubmissions",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "ViewColumns",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "ViewFilters",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "FormFields",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "ViewDefinitions",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "FormTemplates",
                schema: "forms");
        }
    }
}
