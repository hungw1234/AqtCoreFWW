using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateFoundationV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "AiOutputReviews",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    PromptTraceId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOutputReviews", x => x.InternalId);
                    table.UniqueConstraint("AK_AiOutputReviews_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "AiPromptTraces",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PromptHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PromptText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    ClassificationLevel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPromptTraces", x => x.InternalId);
                    table.UniqueConstraint("AK_AiPromptTraces_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DataSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HmacSignature = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SequenceId = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 2)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.InternalId);
                    table.UniqueConstraint("AK_AuditLogEntries_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "AuditRetentionPolicies",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: true),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    ArchiveBeforePurge = table.Column<bool>(type: "bit", nullable: false),
                    ArchivePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AuditRetentionPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompliancePolicies",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Rules = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enforcement = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompliancePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompliancePolicyEvaluations",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompliancePolicyEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginAttempts",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttempts", x => x.InternalId);
                    table.UniqueConstraint("AK_LoginAttempts_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "PersonalDataProcessingLogs",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LegalBasis = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProcessingAction = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalDataProcessingLogs", x => x.InternalId);
                    table.UniqueConstraint("AK_PersonalDataProcessingLogs_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "RtbfRequests",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CitizenNationalId = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    SubjectEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueBy = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProcessedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureLog = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RtbfRequests", x => x.InternalId);
                    table.UniqueConstraint("AK_RtbfRequests_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "SecurityEventLogs",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsSecurityRelevant = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEventLogs", x => x.InternalId);
                    table.UniqueConstraint("AK_SecurityEventLogs_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "SecurityIncidents",
                schema: "audit",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Mitigations = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityIncidents", x => x.InternalId);
                    table.UniqueConstraint("AK_SecurityIncidents_MessageId", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiOutputReviews_Decision",
                schema: "audit",
                table: "AiOutputReviews",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutputReviews_PromptTraceId",
                schema: "audit",
                table: "AiOutputReviews",
                column: "PromptTraceId");

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptTraces_ModelName",
                schema: "audit",
                table: "AiPromptTraces",
                column: "ModelName");

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptTraces_SessionId",
                schema: "audit",
                table: "AiPromptTraces",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptTraces_TenantId_CreatedAt",
                schema: "audit",
                table: "AiPromptTraces",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Action",
                schema: "audit",
                table: "AuditLogEntries",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_ModuleName",
                schema: "audit",
                table: "AuditLogEntries",
                column: "ModuleName");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Severity_OccurredAt",
                schema: "audit",
                table: "AuditLogEntries",
                columns: new[] { "Severity", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_TenantId_Category_OccurredAt",
                schema: "audit",
                table: "AuditLogEntries",
                columns: new[] { "TenantId", "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_TenantId_OccurredAt",
                schema: "audit",
                table: "AuditLogEntries",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_UserId_OccurredAt",
                schema: "audit",
                table: "AuditLogEntries",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRetentionPolicies_TenantId_Category",
                schema: "audit",
                table: "AuditRetentionPolicies",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRetentionPolicy_IsDeleted",
                schema: "audit",
                table: "AuditRetentionPolicies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CompliancePolicies_Category",
                schema: "audit",
                table: "CompliancePolicies",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CompliancePolicies_IsEnabled",
                schema: "audit",
                table: "CompliancePolicies",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_CompliancePolicy_IsDeleted",
                schema: "audit",
                table: "CompliancePolicies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CompliancePolicyEvaluation_IsDeleted",
                schema: "audit",
                table: "CompliancePolicyEvaluations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CompliancePolicyEvaluations_EntityType_EntityId",
                schema: "audit",
                table: "CompliancePolicyEvaluations",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompliancePolicyEvaluations_PolicyId_EvaluatedAt",
                schema: "audit",
                table: "CompliancePolicyEvaluations",
                columns: new[] { "PolicyId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_AttemptedAt",
                schema: "audit",
                table: "LoginAttempts",
                column: "AttemptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_IpAddress_AttemptedAt",
                schema: "audit",
                table: "LoginAttempts",
                columns: new[] { "IpAddress", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_UserId_AttemptedAt",
                schema: "audit",
                table: "LoginAttempts",
                columns: new[] { "UserId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalDataProcessingLogs_DataSubjectId",
                schema: "audit",
                table: "PersonalDataProcessingLogs",
                column: "DataSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalDataProcessingLogs_ProcessorId",
                schema: "audit",
                table: "PersonalDataProcessingLogs",
                column: "ProcessorId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalDataProcessingLogs_TenantId_ProcessedAt",
                schema: "audit",
                table: "PersonalDataProcessingLogs",
                columns: new[] { "TenantId", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RtbfRequests_DataSubjectId",
                schema: "audit",
                table: "RtbfRequests",
                column: "DataSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RtbfRequests_RequestedAt",
                schema: "audit",
                table: "RtbfRequests",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RtbfRequests_TenantId_Status",
                schema: "audit",
                table: "RtbfRequests",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEventLogs_EventType",
                schema: "audit",
                table: "SecurityEventLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEventLogs_IsSecurityRelevant",
                schema: "audit",
                table: "SecurityEventLogs",
                column: "IsSecurityRelevant");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEventLogs_TenantId_OccurredAt",
                schema: "audit",
                table: "SecurityEventLogs",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidents_OccurredAt",
                schema: "audit",
                table: "SecurityIncidents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityIncidents_TenantId_Status",
                schema: "audit",
                table: "SecurityIncidents",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiOutputReviews",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "AiPromptTraces",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "AuditLogEntries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "AuditRetentionPolicies",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "CompliancePolicies",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "CompliancePolicyEvaluations",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "LoginAttempts",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "PersonalDataProcessingLogs",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "RtbfRequests",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "SecurityEventLogs",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "SecurityIncidents",
                schema: "audit");
        }
    }
}
