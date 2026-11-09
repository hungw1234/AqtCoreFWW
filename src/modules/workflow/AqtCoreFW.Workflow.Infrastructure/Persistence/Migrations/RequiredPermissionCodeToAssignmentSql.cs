// LOAD-BEARING: consumed by S2 migration `20260519102348_RequiredPermissionCodeToAssignmentPrep`
// on fresh-DB bootstraps. After S8b column-drop the live integration test that exercised this file
// was retired; the chain Init → S2 (column LIVE) → S8b (column dropped) is still the only path
// new environments traverse. Do NOT modify these constants without re-running the full Workflow
// integration suite against a fresh Testcontainers DB.
namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations;

/// <summary>
/// Plan 260519-0421 v2, Phase 3 — SESSION 2 (additive prep, P2b-style).
///
/// Single source of the RPC→assignment data-migration SQL, shared by the EF
/// migration <c>RequiredPermissionCodeToAssignmentPrep</c> and its Testcontainers
/// integration test (DRY — eliminates the exemplar's duplicate-with-drift-test).
///
/// SCOPE (additive only): copies each non-null
/// <c>WorkflowTransition.RequiredPermissionCode</c> into a
/// <c>WorkflowTransitionAssignment(CombineMode=Union, AllowAssignorOverride=false)</c>
/// plus one <c>ByPermission</c> clause <c>RefsJson = ["code"]</c>, and snapshots the
/// original <c>(transitionId, code)</c> pairs into a backup table. The
/// <c>RequiredPermissionCode</c> column and ALL code that reads it stay LIVE → pre-P3
/// behavior is byte-identical. The column-drop + authz-gate cutover is a later P3
/// session (the failing-default 403 merge-gate enforces atomicity-at-merge).
///
/// Idempotent: dedupe key is <c>(transitionId, code)</c> for the backup and
/// "one assignment per transition" (UX_WorkflowTransitionAssignments_TransitionId)
/// + "one ByPermission clause per assignment" for the transform → rerun = no-op.
///
/// Down is REVERSIBLE here (unlike the later M2 column-drop which is documented
/// irreversible): the column is untouched, so removing the rows this migration
/// inserted and dropping the backup table fully restores prior state.
/// </summary>
public static class RequiredPermissionCodeToAssignmentSql
{
    public const string BackupTable = "[workflow].[WorkflowTransitionRequiredPermissionCodeBackup]";

    // Enum int values pinned from AssignmentCombineMode.Union (0) /
    // AssignmentClauseMode.ByPermission (3). Audit columns inherited from Entity:
    // CreatedAt datetime2(3) NOT NULL, UpdatedAt NULL, IsDeleted bit NOT NULL.
    private const string Preamble = "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET NOCOUNT ON;";

    /// <summary>
    /// 1. Create the recovery archive (idempotent). Not an EF entity — pure DDL.
    /// PAGE-compressed on BOTH the heap and the index: the house invariant
    /// (SchemaUniversalsTests.PageCompression_Script_Applies_To_All_App_Tables) requires
    /// every app-schema table compressed, and apply-page-compression.sql's
    /// `ALTER INDEX ALL REBUILD` does NOT cover a heap — so declare it at creation.
    /// </summary>
    public const string CreateBackupTable = $@"{Preamble}
IF OBJECT_ID(N'workflow.WorkflowTransitionRequiredPermissionCodeBackup', N'U') IS NULL
BEGIN
    CREATE TABLE {BackupTable} (
        [WorkflowTransitionId]   BIGINT         NOT NULL,
        [RequiredPermissionCode] NVARCHAR(100)  NOT NULL,
        [BackedUpAt]             DATETIME2(3)   NOT NULL
    ) WITH (DATA_COMPRESSION = PAGE);
    CREATE INDEX [IX_WTRPCBackup_TransitionId]
        ON {BackupTable} ([WorkflowTransitionId])
        WITH (DATA_COMPRESSION = PAGE);
END";

    /// <summary>
    /// 2. Snapshot original (transitionId, code) pairs BEFORE any transform (idempotent).
    /// Dedupe key is (transitionId, code): because the RPC column stays LIVE, if a
    /// transition's code is mutated between reruns the backup ACCUMULATES every distinct
    /// (transition, code) observed — not only the latest. This is intentionally more
    /// complete (safer for M2 manual recovery, which applies its own per-transition policy).
    /// </summary>
    public const string BackupData = $@"{Preamble}
INSERT INTO {BackupTable} ([WorkflowTransitionId], [RequiredPermissionCode], [BackedUpAt])
SELECT wt.[Id], wt.[RequiredPermissionCode], SYSUTCDATETIME()
FROM [workflow].[WorkflowTransitions] wt
WHERE wt.[RequiredPermissionCode] IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM {BackupTable} b
      WHERE b.[WorkflowTransitionId] = wt.[Id]
        AND b.[RequiredPermissionCode] = wt.[RequiredPermissionCode]);";

    /// <summary>3. One Union assignment per RPC-bearing transition (idempotent — UX per transition).</summary>
    public const string MigrateAssignments = $@"{Preamble}
INSERT INTO [workflow].[WorkflowTransitionAssignments]
    ([WorkflowTransitionId], [CombineMode], [AllowAssignorOverride], [CreatedAt], [UpdatedAt], [IsDeleted])
SELECT wt.[Id], 0, 0, SYSUTCDATETIME(), NULL, 0
FROM [workflow].[WorkflowTransitions] wt
WHERE wt.[RequiredPermissionCode] IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM [workflow].[WorkflowTransitionAssignments] a
      WHERE a.[WorkflowTransitionId] = wt.[Id]);";

    /// <summary>
    /// 4. One ByPermission clause RefsJson=["code"] per such assignment (idempotent).
    /// JSON-escape backslash THEN quote so the value round-trips through
    /// System.Text.Json (WorkflowHandlerResolver.ParseRefs JsonSerializer.Deserialize&lt;List&lt;string&gt;&gt;).
    /// </summary>
    public const string MigrateClauses = $@"{Preamble}
INSERT INTO [workflow].[WorkflowTransitionAssignmentClauses]
    ([AssignmentId], [Mode], [RefsJson], [CreatedAt], [UpdatedAt], [IsDeleted])
SELECT a.[Id], 3,
       N'[""' + REPLACE(REPLACE(wt.[RequiredPermissionCode], '\', '\\'), '""', '\""') + N'""]',
       SYSUTCDATETIME(), NULL, 0
FROM [workflow].[WorkflowTransitionAssignments] a
JOIN [workflow].[WorkflowTransitions] wt ON wt.[Id] = a.[WorkflowTransitionId]
WHERE wt.[RequiredPermissionCode] IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM [workflow].[WorkflowTransitionAssignmentClauses] c
      WHERE c.[AssignmentId] = a.[Id] AND c.[Mode] = 3);";

    /// <summary>
    /// Down (reversible — column LIVE). Removes only what this migration inserted:
    /// ByPermission clauses + their now-clause-less Union assignments whose transition
    /// is in the backup, then drops the backup table. The NOT EXISTS guard preserves
    /// any future multi-clause assignment (none at MVP — RT2-15 — but defensive).
    /// </summary>
    public const string DownCleanup = $@"{Preamble}
IF OBJECT_ID(N'workflow.WorkflowTransitionRequiredPermissionCodeBackup', N'U') IS NOT NULL
BEGIN
    DELETE c
    FROM [workflow].[WorkflowTransitionAssignmentClauses] c
    JOIN [workflow].[WorkflowTransitionAssignments] a ON a.[Id] = c.[AssignmentId]
    WHERE c.[Mode] = 3
      AND EXISTS (
          SELECT 1 FROM {BackupTable} b
          WHERE b.[WorkflowTransitionId] = a.[WorkflowTransitionId]);

    DELETE a
    FROM [workflow].[WorkflowTransitionAssignments] a
    WHERE EXISTS (
              SELECT 1 FROM {BackupTable} b
              WHERE b.[WorkflowTransitionId] = a.[WorkflowTransitionId])
      AND NOT EXISTS (
              SELECT 1 FROM [workflow].[WorkflowTransitionAssignmentClauses] c
              WHERE c.[AssignmentId] = a.[Id]);
END
DROP TABLE IF EXISTS {BackupTable};";

    /// <summary>Ordered Up statements — migration Up() and the test both iterate this.</summary>
    public static IReadOnlyList<string> UpStatements =>
        [CreateBackupTable, BackupData, MigrateAssignments, MigrateClauses];

    /// <summary>Ordered Down statements.</summary>
    public static IReadOnlyList<string> DownStatements => [DownCleanup];
}
