using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Files.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateFoundationV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "files");

            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OutputFormat = table.Column<int>(type: "int", nullable: false),
                    TemplateContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileRecords",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(127)", maxLength: 127, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploaderDisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileVersions",
                schema: "files",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FileRecordId = table.Column<long>(type: "bigint", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileVersions", x => x.InternalId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "files",
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
                name: "RecordLifecycles",
                schema: "files",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FileRecordId = table.Column<long>(type: "bigint", nullable: false),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    RetentionPolicyId = table.Column<long>(type: "bigint", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ScheduledDestroyAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DestroyedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DestroyedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordLifecycles", x => x.InternalId);
                });

            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RetainDays = table.Column<int>(type: "int", nullable: false),
                    ArchiveAfterDays = table.Column<int>(type: "int", nullable: true),
                    DestroyAfterDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplate_IsDeleted",
                schema: "files",
                table: "DocumentTemplates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplate_TenantId_CreatedAt",
                schema: "files",
                table: "DocumentTemplates",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplate_TenantId_Id",
                schema: "files",
                table: "DocumentTemplates",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_TenantId_Status",
                schema: "files",
                table: "DocumentTemplates",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_DocumentTemplates_TenantId_Code",
                schema: "files",
                table: "DocumentTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_FileRecord_PublicId",
                schema: "files",
                table: "FileRecords",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileRecord_IsDeleted",
                schema: "files",
                table: "FileRecords",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FileRecord_TenantId_CreatedAt",
                schema: "files",
                table: "FileRecords",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FileRecord_TenantId_Id",
                schema: "files",
                table: "FileRecords",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_TenantId_Status",
                schema: "files",
                table: "FileRecords",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_UploadedBy",
                schema: "files",
                table: "FileRecords",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "UX_FileRecords_StorageKey",
                schema: "files",
                table: "FileRecords",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_FileVersions_MessageId",
                schema: "files",
                table: "FileVersions",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_TenantId_ContentHash",
                schema: "files",
                table: "FileVersions",
                columns: new[] { "TenantId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "UX_FileVersions_FileRecordId_VersionNumber",
                schema: "files",
                table: "FileVersions",
                columns: new[] { "FileRecordId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_files_ProcessedAt",
                schema: "files",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "AK_RecordLifecycles_MessageId",
                schema: "files",
                table: "RecordLifecycles",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordLifecycles_Status_ScheduledDestroyAt",
                schema: "files",
                table: "RecordLifecycles",
                columns: new[] { "CurrentStatus", "ScheduledDestroyAt" });

            migrationBuilder.CreateIndex(
                name: "UX_RecordLifecycles_FileRecordId",
                schema: "files",
                table: "RecordLifecycles",
                column: "FileRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_TenantId_DocumentType_IsActive",
                schema: "files",
                table: "RetentionPolicies",
                columns: new[] { "TenantId", "DocumentType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicy_IsDeleted",
                schema: "files",
                table: "RetentionPolicies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicy_TenantId_CreatedAt",
                schema: "files",
                table: "RetentionPolicies",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicy_TenantId_Id",
                schema: "files",
                table: "RetentionPolicies",
                columns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTemplates",
                schema: "files");

            migrationBuilder.DropTable(
                name: "FileRecords",
                schema: "files");

            migrationBuilder.DropTable(
                name: "FileVersions",
                schema: "files");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "files");

            migrationBuilder.DropTable(
                name: "RecordLifecycles",
                schema: "files");

            migrationBuilder.DropTable(
                name: "RetentionPolicies",
                schema: "files");
        }
    }
}
