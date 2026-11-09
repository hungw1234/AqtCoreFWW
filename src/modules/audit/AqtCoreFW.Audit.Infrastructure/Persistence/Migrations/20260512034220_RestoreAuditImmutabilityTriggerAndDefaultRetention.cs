using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Restores two QĐ 742 §3.2 / §3.2.b artifacts that were dropped by the Phase-06 migration
    /// regeneration (commits 1f344588 + fde9ec49 deleted the 217 hand-tuned legacy migrations,
    /// and the regenerated <c>InitialCreateFoundationV1</c> snapshot did not carry the raw-SQL
    /// trigger nor the seed <c>InsertData</c> forward):
    ///
    /// 1. <c>[audit].[tr_AuditLog_NoMutate]</c> — AFTER UPDATE/DELETE trigger on
    ///    <c>[audit].[AuditLogEntries]</c> (verbatim from the original
    ///    <c>20260501020000_AddAuditImmutabilityTrigger_M2</c>). Defense-in-depth on top of
    ///    <see cref="AqtCoreFW.Audit.Infrastructure.Persistence.AuditDbContext"/>'s service-layer
    ///    append-only guard — catches anything bypassing EF (raw EXEC, BI tools, db_owner in SSMS).
    ///    Bypass via <c>SESSION_CONTEXT(N'audit_mutate_mode')</c>: <c>N'archive'</c> (purge job
    ///    DELETE past retention) or <c>N'signature'</c> (post-INSERT HMAC UPDATE).
    ///
    /// 2. Global-default <c>AuditRetentionPolicies</c> row (TenantId NULL, Category NULL,
    ///    365-day hot retention, ClassificationLevel = Internal). Resolution precedence at purge
    ///    time falls back to this row; <see cref="AqtCoreFW.Audit.Application.Commands.DeleteAuditRetentionPolicy.DeleteAuditRetentionPolicyCommandHandler"/>
    ///    protects it from deletion by natural key. Post Phase-04b the PK is <c>bigint</c> IDENTITY;
    ///    the row is inserted with an explicit <c>Id = 1</c> (IDENTITY_INSERT) so the seed stays
    ///    deterministic and idempotent across drop+reapply cycles.
    ///
    /// Both statements are guarded so re-running this migration after a manual DROP is a no-op.
    /// </remarks>
    public partial class RestoreAuditImmutabilityTriggerAndDefaultRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- 1. Append-only trigger (CREATE OR ALTER ⇒ idempotent) ---
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER [audit].[tr_AuditLog_NoMutate]
ON [audit].[AuditLogEntries]
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @mode nvarchar(32) =
        ISNULL(CAST(SESSION_CONTEXT(N'audit_mutate_mode') AS nvarchar(32)), N'');

    IF @mode = N'archive' OR @mode = N'signature'
        RETURN;

    RAISERROR(
        'AuditLogEntries is append-only (QĐ 742). Set SESSION_CONTEXT(N''audit_mutate_mode'') to N''archive'' (purge job) or N''signature'' (post-insert HMAC update).',
        16, 1);
    ROLLBACK TRANSACTION;
END;
");

            // --- 2. Global-default retention policy (idempotent; explicit Id = 1) ---
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [audit].[AuditRetentionPolicies] WHERE [TenantId] IS NULL AND [Category] IS NULL)
BEGIN
    SET IDENTITY_INSERT [audit].[AuditRetentionPolicies] ON;
    INSERT INTO [audit].[AuditRetentionPolicies]
        ([Id], [TenantId], [Category], [RetentionDays], [ArchiveBeforePurge], [ArchivePath],
         [CreatedAt], [UpdatedAt], [IsDeleted], [CreatedBy], [ModifiedBy], [ClassificationLevel])
    VALUES
        (1, NULL, NULL, 365, 0, NULL,
         CAST('2026-01-01T00:00:00' AS datetime2(3)), NULL, 0,
         CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier), NULL, 1);
    SET IDENTITY_INSERT [audit].[AuditRetentionPolicies] OFF;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [audit].[tr_AuditLog_NoMutate];");
            migrationBuilder.Sql(
                "DELETE FROM [audit].[AuditRetentionPolicies] WHERE [Id] = 1 AND [TenantId] IS NULL AND [Category] IS NULL;");
        }
    }
}
