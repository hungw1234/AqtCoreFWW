using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// Static helper methods for local SQL Server integration test execution.
/// Handles connection string building, database creation, and data truncation.
/// </summary>
public static partial class LocalSqlHelper
{
    /// <summary>
    /// Builds a connection string for the specified database using local SQL options.
    /// Authenticates as the <c>sa</c> superuser (db_owner, bypasses RLS).
    /// </summary>
    /// <param name="opts">Local SQL configuration.</param>
    /// <param name="dbName">Target database name.</param>
    /// <returns>Connection string with TrustServerCertificate and Encrypt settings.</returns>
    public static string BuildConnectionString(LocalSqlOptions opts, string dbName)
        => BuildConnectionString(opts, dbName, userId: "sa", password: opts.SaPassword);

    /// <summary>
    /// Builds a connection string for the specified database with explicit credentials.
    /// Use for non-privileged (non-db_owner) SQL logins — e.g. <c>rls_test_app</c> — that
    /// are subject to Row-Level Security predicates.
    /// </summary>
    /// <param name="opts">Local SQL configuration (Server, port).</param>
    /// <param name="dbName">Target database name.</param>
    /// <param name="userId">SQL login name (must already exist and have user in <paramref name="dbName"/>).</param>
    /// <param name="password">Login password.</param>
    /// <returns>Connection string with TrustServerCertificate and Encrypt settings.</returns>
    public static string BuildConnectionString(
        LocalSqlOptions opts, string dbName, string userId, string password)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = opts.Server,
            InitialCatalog = dbName,
            UserID = userId,
            Password = password,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            MultipleActiveResultSets = true,
            ConnectTimeout = 30,
        };
        return csb.ConnectionString;
    }

    /// <summary>
    /// Ensures the specified database exists on the SQL Server instance.
    /// Creates it if absent; no-op if already exists.
    /// </summary>
    /// <param name="opts">Local SQL configuration.</param>
    /// <param name="dbName">Database name to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown if dbName contains invalid characters.</exception>
    public static async Task EnsureDatabaseExistsAsync(
        LocalSqlOptions opts,
        string dbName,
        CancellationToken ct = default)
    {
        // Guard against SQL injection via malicious env var or test fixture bug.
        // Allow only alphanumeric and underscore characters in database names.
        if (!SafeDbNameRegex().IsMatch(dbName))
        {
            throw new ArgumentException(
                $"Database name '{dbName}' contains invalid characters. Only A-Z, a-z, 0-9, and _ are allowed.",
                nameof(dbName));
        }

        var masterConnStr = BuildConnectionString(opts, "master");

        await using var conn = new SqlConnection(masterConnStr);
        await conn.OpenAsync(ct);

        // CREATE DATABASE if not exists. Using DB_ID() check for idempotency.
        // Database name is validated above, safe to interpolate.
        var sql = $"IF DB_ID(N'{dbName}') IS NULL CREATE DATABASE [{dbName}]";

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Truncates all data tables in the database while preserving schema.
    /// Skips EF migration history, Hangfire tables, and system tables.
    /// Uses NOCHECK/DELETE/CHECK pattern to handle foreign key constraints.
    /// Afterwards restores migration-embedded seed rows (see
    /// <see cref="ReseedMigrationEmbeddedDataAsync"/>) so the warm database keeps matching
    /// a freshly migrated one.
    /// </summary>
    /// <param name="connString">Connection string to the target database.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task TruncateAllDataAsync(string connString, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(ct);

        // Query all base tables, excluding system and allowlisted tables
        const string tableQuery = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA', 'HangFire')
              AND TABLE_NAME <> '__EFMigrationsHistory'
              AND TABLE_NAME <> 'sysdiagrams'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;

        var tables = new List<(string Schema, string Name)>();

        await using (var queryCmd = new SqlCommand(tableQuery, conn))
        {
            await using var reader = await queryCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        if (tables.Count == 0)
            return;

        // Audit M2 trigger (tr_AuditLog_NoMutate) blocks DELETE on audit.AuditLogEntries
        // unless SESSION_CONTEXT('audit_mutate_mode') = 'archive'. Set it once on this
        // connection so the truncate loop below succeeds. The trigger uses ROLLBACK
        // TRANSACTION on rejection — without this bypass the outer transaction zombies
        // and downstream rollback/commit fails. Connection-scoped: cleared on Dispose.
        await using (var bypassCmd = new SqlCommand(
            "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'archive', @read_only=0;",
            conn))
        {
            await bypassCmd.ExecuteNonQueryAsync(ct);
        }

        // Execute truncation in a transaction: disable FKs → delete → re-enable FKs
        await using var transaction = conn.BeginTransaction();

        try
        {
            // Step 1: Disable all FK constraints
            foreach (var (schema, name) in tables)
            {
                var disableSql = $"ALTER TABLE [{schema}].[{name}] NOCHECK CONSTRAINT ALL";
                await using var cmd = new SqlCommand(disableSql, conn, transaction);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Step 2: Delete from all tables
            var skippedTables = new List<string>();
            foreach (var (schema, name) in tables)
            {
                try
                {
                    var deleteSql = $"DELETE FROM [{schema}].[{name}]";
                    await using var cmd = new SqlCommand(deleteSql, conn, transaction);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException)
                {
                    // Log and continue — some tables may have constraints we can't disable
                    skippedTables.Add($"[{schema}].[{name}]");
                }
            }

            // Step 3: Re-enable FK constraints with validation
            foreach (var (schema, name) in tables)
            {
                var enableSql = $"ALTER TABLE [{schema}].[{name}] WITH CHECK CHECK CONSTRAINT ALL";
                await using var cmd = new SqlCommand(enableSql, conn, transaction);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);

            // Log skipped tables for debugging (console visible in test output)
            if (skippedTables.Count > 0)
            {
                Console.WriteLine($"[LocalSqlHelper] Skipped tables during truncate: {string.Join(", ", skippedTables)}");
            }
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        // Truncate wiped migration-embedded seed rows too, and those never come back on a
        // warm-reuse database: the seeding migrations are already recorded in
        // __EFMigrationsHistory, so app boot will not re-run them. Restore them here so the
        // warm database matches what a freshly migrated database contains.
        await ReseedMigrationEmbeddedDataAsync(conn, ct);
    }

    /// <summary>
    /// Re-inserts the seed rows that live INSIDE migrations (raw SQL or HasData) and are
    /// therefore lost forever once <see cref="TruncateAllDataAsync"/> deletes them on a
    /// warm-reuse database. Idempotent — every batch is IF-NOT-EXISTS guarded.
    ///
    /// Inventory (keep in sync when a migration gains embedded seed data; the seed-assert
    /// tests, e.g. AuditLogImmutabilityTests.GlobalDefaultRetentionPolicy_RowExists, are the
    /// drift guard):
    ///   1. audit.AuditRetentionPolicies global default — migration
    ///      20260512034220_RestoreAuditImmutabilityTriggerAndDefaultRetention (raw SQL).
    ///   2. audit.ClassificationRules 4 rows — ClassificationRuleConfiguration.HasData.
    ///   3. identity.MfaPolicies 3 system rows — MfaPolicyConfiguration.HasData (QĐ742 1.6.a).
    /// Workflow backfill migrations derive data from existing rows — nothing to restore.
    /// RowVersion columns are SQL rowversion (DB-managed) and are excluded from inserts.
    /// </summary>
    private static async Task ReseedMigrationEmbeddedDataAsync(SqlConnection conn, CancellationToken ct)
    {
        // 1. Global-default audit retention policy (values verbatim from migration 20260512034220).
        const string retentionSeed = """
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
            """;

        // 2. Classification rules (values verbatim from ClassificationRuleConfiguration.HasData:
        //    Public/Internal/Confidential/Restricted = Level 0..3).
        const string classificationSeed = """
            IF NOT EXISTS (SELECT 1 FROM [audit].[ClassificationRules])
            BEGIN
                INSERT INTO [audit].[ClassificationRules]
                    ([Level], [MustEncryptAtRest], [AllowExport], [MaxRetentionDays], [UpdatedBy], [UpdatedAt])
                VALUES
                    (0, 0, 1, NULL, N'system', CAST('2026-05-10T00:00:00' AS datetime2(3))),
                    (1, 0, 1, 365,  N'system', CAST('2026-05-10T00:00:00' AS datetime2(3))),
                    (2, 1, 0, 1825, N'system', CAST('2026-05-10T00:00:00' AS datetime2(3))),
                    (3, 1, 0, 365,  N'system', CAST('2026-05-10T00:00:00' AS datetime2(3)));
            END;
            """;

        // 3. MFA system policies (values verbatim from MfaPolicyConfiguration.HasData;
        //    AllowedFactors: Totp=1, Totp|EmailOtp=3; ClassificationLevel Internal=1).
        const string mfaPolicySeed = """
            IF NOT EXISTS (SELECT 1 FROM [identity].[MfaPolicies])
            BEGIN
                SET IDENTITY_INSERT [identity].[MfaPolicies] ON;
                INSERT INTO [identity].[MfaPolicies]
                    ([Id], [PublicId], [Role], [IsMandatory], [GraceDays], [AllowedFactors],
                     [CreatedAt], [UpdatedAt], [IsDeleted], [CreatedBy], [ModifiedBy], [ClassificationLevel])
                VALUES
                    (1, CAST('018f5e80-0001-7001-8000-000000000001' AS uniqueidentifier), N'SystemAdmin', 1, 0, 1,
                     CAST('2026-05-01T00:00:00' AS datetime2(3)), NULL, 0,
                     CAST('00000000-0000-0000-0000-000000000001' AS uniqueidentifier), NULL, 1),
                    (2, CAST('018f5e80-0001-7001-8000-000000000002' AS uniqueidentifier), N'Admin', 1, 7, 1,
                     CAST('2026-05-01T00:00:00' AS datetime2(3)), NULL, 0,
                     CAST('00000000-0000-0000-0000-000000000001' AS uniqueidentifier), NULL, 1),
                    (3, CAST('018f5e80-0001-7001-8000-000000000003' AS uniqueidentifier), N'GovOfficer', 0, 7, 3,
                     CAST('2026-05-01T00:00:00' AS datetime2(3)), NULL, 0,
                     CAST('00000000-0000-0000-0000-000000000001' AS uniqueidentifier), NULL, 1);
                SET IDENTITY_INSERT [identity].[MfaPolicies] OFF;
            END;
            """;

        // Guard per-batch in C#: TruncateAllDataAsync serves several fixtures pointing at
        // DIFFERENT databases (Main / Messaging / RulesTenantIso / SodConflictChecker), and
        // only Main carries every schema. A batch referencing a missing table fails at
        // batch-compile time with error 208 even inside an unreachable IF, so the existence
        // check must happen OUTSIDE the batch that references the table.
        var guardedBatches = new (string Table, string Sql)[]
        {
            ("[audit].[AuditRetentionPolicies]", retentionSeed),
            ("[audit].[ClassificationRules]", classificationSeed),
            ("[identity].[MfaPolicies]", mfaPolicySeed),
        };

        foreach (var (table, sql) in guardedBatches)
        {
            await using (var checkCmd = new SqlCommand("SELECT OBJECT_ID(@table)", conn))
            {
                checkCmd.Parameters.AddWithValue("@table", table);
                if (await checkCmd.ExecuteScalarAsync(ct) is DBNull or null)
                    continue; // table absent in this fixture's database — nothing to reseed
            }

            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Regex to validate database names: only alphanumeric and underscore allowed.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex SafeDbNameRegex();
}
