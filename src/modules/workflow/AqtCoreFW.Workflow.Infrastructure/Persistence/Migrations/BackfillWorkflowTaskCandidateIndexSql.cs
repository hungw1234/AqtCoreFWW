namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Migrations;

/// <summary>
/// SQL constants for the WorkflowTaskCandidateIndex backfill data migration.
///
/// Scope: for every open/in-progress pool task (AssigneeId IS NULL, non-null snapshot,
/// Open=0/InProgress=1 status, not soft-deleted) that has NO index rows, insert one row
/// per predicate extracted from CandidateRefsJson.
///
/// The snapshot wire shape is [{"t":int,"r":"string","c":int}] produced by
/// CandidatePrincipalSerializer since commit c9dfd94d — refs are already canonical
/// (Trim+ToUpperInvariant at save time) so $.r is lifted raw without re-canonicalization.
///
/// Idempotent: soft-delete-aware NOT EXISTS guard + @migrationStart bound prevents
/// double-insertion on re-run and avoids racing the live producer that now writes rows
/// for tasks created after the fix.
///
/// Down is a no-op: producer-written and backfilled rows are indistinguishable post-fix.
/// Deleting backfilled rows would incorrectly render open pool tasks invisible again.
/// This is an intentional forward-only data fix.
///
/// LOAD-BEARING: consumed by migration BackfillWorkflowTaskCandidateIndexFromSnapshot.
/// The migration and the integration test in WorkflowBackfillCandidateIndexMigrationTests
/// share this source (DRY — eliminates drift).
/// </summary>
public static class BackfillWorkflowTaskCandidateIndexSql
{
    private const string Preamble = "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET NOCOUNT ON;";

    /// <summary>
    /// Backfill Up statement:
    /// 1. Guard: must run as db_owner to bypass RLS BLOCK-AFTER-INSERT predicate.
    /// 2. Bound timestamp prevents touching tasks the live producer already indexed.
    /// 3. OPENJSON extracts each predicate from CandidateRefsJson snapshot.
    /// 4. NOT EXISTS (soft-delete-aware) ensures idempotency on re-run.
    /// 5. All NOT NULL columns (Id excluded — IDENTITY; UpdatedAt nullable, omitted).
    /// </summary>
    public const string Backfill = $@"{Preamble}
IF IS_MEMBER('db_owner') = 0
    THROW 50000, 'CandidateIndex backfill requires db_owner (RLS BLOCK-AFTER-INSERT bypass).', 1;

DECLARE @migrationStart datetime2 = SYSUTCDATETIME();

INSERT INTO [workflow].[WorkflowTaskCandidateIndex]
    ([WorkflowTaskId], [ClauseId], [CombineMode], [PrincipalType], [PrincipalRefId], [TenantId], [CreatedAt], [IsDeleted])
SELECT
    t.[Id],
    0,          -- ClauseId: resolver aggregates by (Type,Ref); no clause provenance in snapshot
    j.[c],      -- CombineMode int (AssignmentCombineMode ordinal)
    j.[t],      -- PrincipalType int (CandidatePrincipalType ordinal)
    j.[r],      -- PrincipalRefId canonical string (Trim+ToUpperInvariant since c9dfd94d)
    t.[TenantId],
    SYSUTCDATETIME(),
    0           -- IsDeleted
FROM [workflow].[WorkflowTasks] t
CROSS APPLY OPENJSON(t.[CandidateRefsJson])
    WITH (
        [t] int    '$.t',
        [r] nvarchar(64) '$.r',
        [c] int    '$.c'
    ) j
WHERE t.[AssigneeId] IS NULL
  AND t.[CandidateRefsJson] IS NOT NULL
  AND ISJSON(t.[CandidateRefsJson]) = 1
  AND t.[Status] IN (0, 1)               -- Open=0, InProgress=1 (verified ordinals)
  AND t.[IsDeleted] = 0
  AND t.[CreatedAt] < @migrationStart    -- don't overlap with live producer post-fix
  AND NOT EXISTS (
      SELECT 1
      FROM [workflow].[WorkflowTaskCandidateIndex] x
      WHERE x.[WorkflowTaskId] = t.[Id]
        AND x.[PrincipalType] = j.[t]
        AND x.[PrincipalRefId] = j.[r]
        AND x.[IsDeleted] = 0
  );";

    /// <summary>
    /// Down is intentionally a no-op.
    /// Producer-written rows and backfill rows are indistinguishable post-fix.
    /// Removing them would render open pool tasks invisible again — worse than the original gap.
    /// Rollback strategy: re-run Up (idempotent); or restore from a pre-migration backup.
    /// </summary>
    public const string DownNoOp = $@"{Preamble}
-- Down intentionally no-op: forward-only data fix.
-- Backfilled rows are indistinguishable from producer-written rows post-fix.
-- Deleting them would re-introduce the original P1 invisible-pool-task gap.
PRINT 'BackfillWorkflowTaskCandidateIndexFromSnapshot: Down is a no-op (forward-only data fix).';";

    /// <summary>Ordered Up statements.</summary>
    public static IReadOnlyList<string> UpStatements => [Backfill];

    /// <summary>Ordered Down statements (no-op).</summary>
    public static IReadOnlyList<string> DownStatements => [DownNoOp];
}
