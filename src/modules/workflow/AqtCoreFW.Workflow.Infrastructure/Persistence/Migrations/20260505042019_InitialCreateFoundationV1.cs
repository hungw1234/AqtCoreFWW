using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateFoundationV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "DelegationRules",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_DelegationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EscalationRules",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    StateId = table.Column<long>(type: "bigint", nullable: true),
                    EscalationAfterMinutes = table.Column<int>(type: "int", nullable: false),
                    EscalateTo = table.Column<int>(type: "int", nullable: false),
                    EscalateToValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NotifyVia = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
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
                    table.PrimaryKey("PK_EscalationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "workflow",
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
                name: "TaskAssignments",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowTaskId = table.Column<long>(type: "bigint", nullable: false),
                    AssignedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VersionMigrations",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    TargetDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    StateMappingsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    MappingHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalInstances = table.Column<int>(type: "int", nullable: false),
                    MigratedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false),
                    HangfireJobId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_VersionMigrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowAssignmentRules",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntitySubType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Specificity = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAssignmentRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitions",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "Case"),
                    DefinitionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    IsLatest = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstanceBranchStatuses",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ParallelBranchId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstanceBranchStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstances",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    CurrentStateId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CurrentStateEnteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SlaBreachNotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalDefinitionId = table.Column<long>(type: "bigint", nullable: true),
                    LastMigrationId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowNotificationConfigs",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    ActionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecipientType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecipientValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BodyTemplate = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
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
                    table.PrimaryKey("PK_WorkflowNotificationConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowParallelBranches",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    BranchingStateId = table.Column<long>(type: "bigint", nullable: false),
                    ContinuationStateId = table.Column<long>(type: "bigint", nullable: false),
                    BranchType = table.Column<int>(type: "int", nullable: false),
                    RequiredCount = table.Column<int>(type: "int", nullable: true),
                    AllowCancelOnAnyReject = table.Column<bool>(type: "bit", nullable: false),
                    TimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowParallelBranches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTasks",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchChildStatusId = table.Column<long>(type: "bigint", nullable: true),
                    DelegatedFromUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTasks", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitionAttachments",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanceHistoryId = table.Column<long>(type: "bigint", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitionAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VersionMigrationItems",
                schema: "workflow",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    MigrationId = table.Column<long>(type: "bigint", nullable: false),
                    InstanceId = table.Column<long>(type: "bigint", nullable: false),
                    OldStateId = table.Column<long>(type: "bigint", nullable: false),
                    NewStateId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionMigrationItems", x => x.InternalId);
                    table.ForeignKey(
                        name: "FK_VersionMigrationItems_VersionMigrations_MigrationId",
                        column: x => x.MigrationId,
                        principalSchema: "workflow",
                        principalTable: "VersionMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStates",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayNameVi = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SlaHours = table.Column<int>(type: "int", nullable: true),
                    AutoTransitionOnTimeoutId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStates_WorkflowDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitions",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    FromStateId = table.Column<long>(type: "bigint", nullable: false),
                    ToStateId = table.Column<long>(type: "bigint", nullable: false),
                    ActionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActionLabelVi = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActionLabelEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiredPermissionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConditionsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsRecall = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitions_WorkflowDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowBranchChildStatuses",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanceBranchStatusId = table.Column<long>(type: "bigint", nullable: false),
                    BranchChildId = table.Column<long>(type: "bigint", nullable: false),
                    ResolutionType = table.Column<int>(type: "int", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBranchChildStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowBranchChildStatuses_WorkflowInstanceBranchStatuses_InstanceBranchStatusId",
                        column: x => x.InstanceBranchStatusId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowInstanceBranchStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstanceHistories",
                schema: "workflow",
                columns: table => new
                {
                    InternalId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    InstanceId = table.Column<long>(type: "bigint", nullable: false),
                    FromStateId = table.Column<long>(type: "bigint", nullable: false),
                    ToStateId = table.Column<long>(type: "bigint", nullable: false),
                    ActionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransitionedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TransitionedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstanceHistories", x => x.InternalId);
                    table.ForeignKey(
                        name: "FK_WorkflowInstanceHistories_WorkflowInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowBranchChildren",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParallelBranchId = table.Column<long>(type: "bigint", nullable: false),
                    TargetStateId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayNameVi = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DepartmentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultAssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBranchChildren", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowBranchChildren_WorkflowParallelBranches_ParallelBranchId",
                        column: x => x.ParallelBranchId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowParallelBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowBranchResolutions",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchChildId = table.Column<long>(type: "bigint", nullable: false),
                    ResolutionType = table.Column<int>(type: "int", nullable: false),
                    ResultingStateId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBranchResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowBranchResolutions_WorkflowBranchChildren_BranchChildId",
                        column: x => x.BranchChildId,
                        principalSchema: "workflow",
                        principalTable: "WorkflowBranchChildren",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRule_IsDeleted",
                schema: "workflow",
                table: "DelegationRules",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRule_TenantId_CreatedAt",
                schema: "workflow",
                table: "DelegationRules",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRule_TenantId_Id",
                schema: "workflow",
                table: "DelegationRules",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRules_TenantId_DelegatorUserId_IsActive",
                schema: "workflow",
                table: "DelegationRules",
                columns: new[] { "TenantId", "DelegatorUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRules_TenantId_ValidUntil",
                schema: "workflow",
                table: "DelegationRules",
                columns: new[] { "TenantId", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_EscalationRule_IsDeleted",
                schema: "workflow",
                table: "EscalationRules",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EscalationRule_TenantId_CreatedAt",
                schema: "workflow",
                table: "EscalationRules",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_EscalationRule_TenantId_Id",
                schema: "workflow",
                table: "EscalationRules",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EscalationRules_DefinitionId_IsActive",
                schema: "workflow",
                table: "EscalationRules",
                columns: new[] { "WorkflowDefinitionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_workflow_ProcessedAt",
                schema: "workflow",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignment_IsDeleted",
                schema: "workflow",
                table: "TaskAssignments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_TaskId_AssignedAt",
                schema: "workflow",
                table: "TaskAssignments",
                columns: new[] { "WorkflowTaskId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "AK_WorkflowVersionMigrationItem_MessageId",
                schema: "workflow",
                table: "VersionMigrationItems",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VersionMigrationItems_MigrationId_Status",
                schema: "workflow",
                table: "VersionMigrationItems",
                columns: new[] { "MigrationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_VersionMigrationItems_MigrationId_InstanceId",
                schema: "workflow",
                table: "VersionMigrationItems",
                columns: new[] { "MigrationId", "InstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VersionMigrations_TenantId_SourceDefId_Status",
                schema: "workflow",
                table: "VersionMigrations",
                columns: new[] { "TenantId", "SourceDefinitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersionMigration_IsDeleted",
                schema: "workflow",
                table: "VersionMigrations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersionMigration_TenantId_CreatedAt",
                schema: "workflow",
                table: "VersionMigrations",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersionMigration_TenantId_Id",
                schema: "workflow",
                table: "VersionMigrations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignmentRule_IsDeleted",
                schema: "workflow",
                table: "WorkflowAssignmentRules",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignmentRule_TenantId_CreatedAt",
                schema: "workflow",
                table: "WorkflowAssignmentRules",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignmentRule_TenantId_Id",
                schema: "workflow",
                table: "WorkflowAssignmentRules",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignmentRules_TenantId_EntityType_SubType_IsActive",
                schema: "workflow",
                table: "WorkflowAssignmentRules",
                columns: new[] { "TenantId", "EntityType", "EntitySubType", "IsActive" })
                .Annotation("SqlServer:Include", new[] { "WorkflowDefinitionId", "Specificity", "Priority", "EffectiveFrom", "EffectiveUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChild_IsDeleted",
                schema: "workflow",
                table: "WorkflowBranchChildren",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChildren_ParallelBranchId",
                schema: "workflow",
                table: "WorkflowBranchChildren",
                column: "ParallelBranchId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowBranchChildren_ParallelBranchId_Name",
                schema: "workflow",
                table: "WorkflowBranchChildren",
                columns: new[] { "ParallelBranchId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChildStatus_IsDeleted",
                schema: "workflow",
                table: "WorkflowBranchChildStatuses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchChildStatuses_InstanceBranchStatusId",
                schema: "workflow",
                table: "WorkflowBranchChildStatuses",
                column: "InstanceBranchStatusId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowBranchChildStatuses_BranchStatusId_ChildId",
                schema: "workflow",
                table: "WorkflowBranchChildStatuses",
                columns: new[] { "InstanceBranchStatusId", "BranchChildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchResolution_IsDeleted",
                schema: "workflow",
                table: "WorkflowBranchResolutions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBranchResolutions_BranchChildId",
                schema: "workflow",
                table: "WorkflowBranchResolutions",
                column: "BranchChildId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowBranchResolutions_ChildId_ResolutionType",
                schema: "workflow",
                table: "WorkflowBranchResolutions",
                columns: new[] { "BranchChildId", "ResolutionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_WorkflowDefinition_PublicId",
                schema: "workflow",
                table: "WorkflowDefinitions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_IsDeleted",
                schema: "workflow",
                table: "WorkflowDefinitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId_Key_IsLatest",
                schema: "workflow",
                table: "WorkflowDefinitions",
                columns: new[] { "TenantId", "DefinitionKey", "IsLatest" },
                filter: "[IsLatest] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId_Key_Version",
                schema: "workflow",
                table: "WorkflowDefinitions",
                columns: new[] { "TenantId", "DefinitionKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId_Name",
                schema: "workflow",
                table: "WorkflowDefinitions",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceBranchStatus_IsDeleted",
                schema: "workflow",
                table: "WorkflowInstanceBranchStatuses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceBranchStatuses_InstanceId",
                schema: "workflow",
                table: "WorkflowInstanceBranchStatuses",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowInstanceBranchStatuses_InstanceId_BranchId",
                schema: "workflow",
                table: "WorkflowInstanceBranchStatuses",
                columns: new[] { "InstanceId", "ParallelBranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_WorkflowInstanceHistory_MessageId",
                schema: "workflow",
                table: "WorkflowInstanceHistories",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceHistories_InstanceId_TransitionedAt",
                schema: "workflow",
                table: "WorkflowInstanceHistories",
                columns: new[] { "InstanceId", "TransitionedAt" });

            migrationBuilder.CreateIndex(
                name: "AK_WorkflowInstance_PublicId",
                schema: "workflow",
                table: "WorkflowInstances",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_IsDeleted",
                schema: "workflow",
                table: "WorkflowInstances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_EntityType_EntityId",
                schema: "workflow",
                table: "WorkflowInstances",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_LastMigrationId",
                schema: "workflow",
                table: "WorkflowInstances",
                column: "LastMigrationId",
                filter: "[LastMigrationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_OriginalDefinitionId",
                schema: "workflow",
                table: "WorkflowInstances",
                column: "OriginalDefinitionId",
                filter: "[OriginalDefinitionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_TenantId_CompletedAt_SlaBreach",
                schema: "workflow",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "CompletedAt", "SlaBreachNotifiedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowInstances_Active_Entity",
                schema: "workflow",
                table: "WorkflowInstances",
                columns: new[] { "EntityType", "EntityId", "TenantId" },
                unique: true,
                filter: "[CompletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationConfigs_DefId_Action_Active",
                schema: "workflow",
                table: "WorkflowNotificationConfigs",
                columns: new[] { "DefinitionId", "ActionName", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationConfig_IsDeleted",
                schema: "workflow",
                table: "WorkflowNotificationConfigs",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationConfig_TenantId_CreatedAt",
                schema: "workflow",
                table: "WorkflowNotificationConfigs",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotificationConfig_TenantId_Id",
                schema: "workflow",
                table: "WorkflowNotificationConfigs",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowParallelBranch_IsDeleted",
                schema: "workflow",
                table: "WorkflowParallelBranches",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowParallelBranches_DefinitionId",
                schema: "workflow",
                table: "WorkflowParallelBranches",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowParallelBranches_DefinitionId_BranchingStateId",
                schema: "workflow",
                table: "WorkflowParallelBranches",
                columns: new[] { "DefinitionId", "BranchingStateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowState_IsDeleted",
                schema: "workflow",
                table: "WorkflowStates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStates_DefinitionId_IsInitial",
                schema: "workflow",
                table: "WorkflowStates",
                columns: new[] { "DefinitionId", "IsInitial" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowStates_DefinitionId_Name",
                schema: "workflow",
                table: "WorkflowStates",
                columns: new[] { "DefinitionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "CIX_WorkflowTask_CreatedAt_Id",
                schema: "workflow",
                table: "WorkflowTasks",
                columns: new[] { "CreatedAt", "Id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTask_IsDeleted",
                schema: "workflow",
                table: "WorkflowTasks",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTask_TenantId_CreatedAt",
                schema: "workflow",
                table: "WorkflowTasks",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTask_TenantId_Id",
                schema: "workflow",
                table: "WorkflowTasks",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_BranchChildStatusId",
                schema: "workflow",
                table: "WorkflowTasks",
                column: "BranchChildStatusId",
                filter: "[BranchChildStatusId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_DelegatedFromUserId",
                schema: "workflow",
                table: "WorkflowTasks",
                column: "DelegatedFromUserId",
                filter: "[DelegatedFromUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_InstanceId_Status",
                schema: "workflow",
                table: "WorkflowTasks",
                columns: new[] { "WorkflowInstanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTasks_TenantId_AssigneeId_Status",
                schema: "workflow",
                table: "WorkflowTasks",
                columns: new[] { "TenantId", "AssigneeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionAttachment_IsDeleted",
                schema: "workflow",
                table: "WorkflowTransitionAttachments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionAttachments_InstanceHistoryId",
                schema: "workflow",
                table: "WorkflowTransitionAttachments",
                column: "InstanceHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransition_IsDeleted",
                schema: "workflow",
                table: "WorkflowTransitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitions_DefinitionId_FromStateId",
                schema: "workflow",
                table: "WorkflowTransitions",
                columns: new[] { "DefinitionId", "FromStateId" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowTransitions_DefinitionId_FromState_Action",
                schema: "workflow",
                table: "WorkflowTransitions",
                columns: new[] { "DefinitionId", "FromStateId", "ActionName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationRules",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "EscalationRules",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "TaskAssignments",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "VersionMigrationItems",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowAssignmentRules",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowBranchChildStatuses",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowBranchResolutions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowInstanceHistories",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowNotificationConfigs",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowStates",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowTasks",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowTransitionAttachments",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowTransitions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "VersionMigrations",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowInstanceBranchStatuses",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowBranchChildren",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowInstances",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "WorkflowParallelBranches",
                schema: "workflow");
        }
    }
}
