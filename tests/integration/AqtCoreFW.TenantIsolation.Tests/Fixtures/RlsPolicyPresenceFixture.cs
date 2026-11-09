using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace AqtCoreFW.TenantIsolation.Tests.Fixtures;

/// <summary>
/// Fixture that applies all 14 module migrations to a single test database and exposes an
/// ADO.NET connection string for <see cref="RlsPolicyPresenceTests"/>.
///
/// All 14 contexts target the same DB; each uses its own per-schema __EFMigrationsHistory
/// table so they don't collide on migration tracking.
///
/// Contexts that do NOT accept ITenantContext (AuditDbContext, IdentityDbContext) are
/// instantiated via their design-time ctor (DbContextOptions only). Contexts requiring
/// IKeyResolver (FilesDbContext) pass null — RLS DDL migrations don't touch encrypted columns.
///
/// After migrations, a non-privileged SQL login <c>rls_test_app</c> is created idempotently
/// with SELECT/INSERT/UPDATE/DELETE on the tables covered by nullable-TenantId RLS policies
/// (<c>audit.AuditLogEntries</c>, <c>audit.SecurityEventLogs</c>, <c>config.SystemParameters</c>),
/// plus read-only grants on adjacent tables the least-privilege read paths touch
/// (identity signing, forms public-read, and <c>audit.AuditRetentionPolicies</c>) — see the grant list
/// in <see cref="CreateRlsTestAppLoginAsync"/> for the authoritative surface.
/// Enforcement tests use <see cref="ConstrainedConnectionString"/> to prove the predicates fire
/// for callers outside <c>db_owner</c>.
/// </summary>
public sealed class RlsPolicyPresenceFixture : IAsyncLifetime
{
    private const string DbName = "AqtCoreFW_Test_RlsPresence";

    // Credentials for the non-privileged test login.
    // NOT db_owner — that's the entire point of the enforcement tests.
    internal const string RlsTestLogin    = "rls_test_app";
    internal const string RlsTestPassword = "RlsTest@1234!";

    // C3 schema-GRANT isolation spike: a SECOND non-db_owner login granted ONLY on schema [files].
    // Contrast with rls_test_app (granted across audit+config+identity+forms) — the delta between the two
    // connections is the per-service isolation proof. See plans/260713-0935-c3-schema-grant-isolation-spike.
    internal const string FilesSvcLogin    = "files_svc_app";
    internal const string FilesSvcPassword = "FilesSvc@1234!";

    private readonly LocalSqlOptions _opts = LocalSqlOptions.FromEnvironment();

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Gov@Test1234!")
        .Build();

    private string? _connectionString;
    private string? _constrainedConnectionString;
    private string? _filesSvcConnectionString;

    /// <summary>ADO.NET connection string authenticated as <c>sa</c> (db_owner, bypasses RLS).</summary>
    public string ConnectionString => _connectionString
        ?? throw new InvalidOperationException("Fixture not initialized — call InitializeAsync first.");

    /// <summary>
    /// ADO.NET connection string authenticated as <c>rls_test_app</c> (non-db_owner).
    /// RLS FILTER and BLOCK predicates are fully evaluated on connections using this string.
    /// </summary>
    public string ConstrainedConnectionString => _constrainedConnectionString
        ?? throw new InvalidOperationException("Fixture not initialized — call InitializeAsync first.");

    /// <summary>
    /// ADO.NET connection string authenticated as <c>files_svc_app</c> (non-db_owner, granted only
    /// <c>SELECT ON SCHEMA::[files]</c>). Used by the C3 schema-GRANT isolation spike to prove per-service
    /// read-isolation on the shared all-schemas DB.
    /// </summary>
    public string FilesSvcConnectionString => _filesSvcConnectionString
        ?? throw new InvalidOperationException("Fixture not initialized — call InitializeAsync first.");

    public async Task InitializeAsync()
    {
        if (_opts.IsEnabled)
        {
            await LocalSqlHelper.EnsureDatabaseExistsAsync(_opts, DbName);
            _connectionString = LocalSqlHelper.BuildConnectionString(_opts, DbName);
        }
        else
        {
            await _container.StartAsync();
            var csb = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                InitialCatalog         = DbName,   // Testcontainers returns "master" by default; fix to AqtCoreFW_Test_RlsPresence so CREATE USER + GRANTs run in the right DB.
                TrustServerCertificate = true,
                Encrypt                = SqlConnectionEncryptOption.Optional,
            };
            _connectionString = csb.ConnectionString;
        }

        // Apply all 14 module migrations in dependency-friendly order.
        // NullTenantContext (IsSystemAdmin=true) bypasses RLS global filters during DDL.
        var tenantCtx = new TestTenantContext(null, isSystemAdmin: true);

        // Contexts with (DbContextOptions<T>, ITenantContext) ctor — most modules
        await MigrateAsync(new AiDbContext(Opts<AiDbContext>("ai"), tenantCtx));
        await MigrateAsync(new BusinessProcessDbContext(Opts<BusinessProcessDbContext>("businessprocess"), tenantCtx));
        await MigrateAsync(new CollaborationDbContext(Opts<CollaborationDbContext>("collab"), tenantCtx));
        await MigrateAsync(new FormsDbContext(Opts<FormsDbContext>("forms"), tenantCtx));
        await MigrateAsync(new IntegrationDbContext(Opts<IntegrationDbContext>("integration"), tenantCtx));
        await MigrateAsync(new MasterDataDbContext(Opts<MasterDataDbContext>("masterdata"), tenantCtx));
        await MigrateAsync(new NotificationsDbContext(Opts<NotificationsDbContext>("notifications"), tenantCtx));
        await MigrateAsync(new OrgDbContext(Opts<OrgDbContext>("organization"), tenantCtx));
        await MigrateAsync(new ReportingDbContext(Opts<ReportingDbContext>("reporting"), tenantCtx));
        await MigrateAsync(new RulesDbContext(Opts<RulesDbContext>("rules"), tenantCtx));
        await MigrateAsync(new SystemParamsDbContext(Opts<SystemParamsDbContext>("config"), tenantCtx));
        await MigrateAsync(new WorkflowDbContext(Opts<WorkflowDbContext>("workflow"), tenantCtx));

        // Contexts with (DbContextOptions<T>, ITenantContext, IKeyResolver?) — Files module
        await MigrateAsync(new FilesDbContext(Opts<FilesDbContext>("files"), tenantCtx, null));

        // Contexts with design-time ctor only — no ITenantContext (Audit, Identity)
        await MigrateAsync(new AuditDbContext(Opts<AuditDbContext>("audit")));
        await MigrateAsync(new IdentityDbContext(Opts<IdentityDbContext>("identity")));

        // Create the non-privileged logins + users + minimal grants after all migrations.
        // Must run after migrations so schemas and tables are guaranteed to exist.
        await CreateRlsTestAppLoginAsync();
        await CreateFilesSvcAppLoginAsync();

        // Build constrained connection strings after the logins are created.
        _constrainedConnectionString = BuildLoginConnectionString(RlsTestLogin, RlsTestPassword);
        _filesSvcConnectionString    = BuildLoginConnectionString(FilesSvcLogin, FilesSvcPassword);
    }

    /// <summary>Builds an ADO.NET connection string for a specific non-db_owner login against the test DB.</summary>
    private string BuildLoginConnectionString(string login, string password)
    {
        if (_opts.IsEnabled)
            return LocalSqlHelper.BuildConnectionString(_opts, DbName, userId: login, password: password);

        // Testcontainers path: same server as the admin connection, different credentials.
        var adminCsb = new SqlConnectionStringBuilder(_connectionString!);
        return new SqlConnectionStringBuilder
        {
            DataSource             = adminCsb.DataSource,
            InitialCatalog         = DbName,
            UserID                 = login,
            Password               = password,
            TrustServerCertificate = true,
            Encrypt                = SqlConnectionEncryptOption.Optional,
        }.ConnectionString;
    }

    public Task DisposeAsync()
    {
        if (_opts.IsEnabled)
            return Task.CompletedTask; // persistent DB — keep for re-use across runs

        return _container.StopAsync();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Idempotently creates SQL login <c>rls_test_app</c> on master, then a DB user in the test
    /// database, then grants SELECT/INSERT/UPDATE/DELETE on the three nullable-TenantId tables.
    ///
    /// GRANT is idempotent in SQL Server (re-granting an existing grant is a no-op).
    /// CREATE LOGIN / CREATE USER are guarded by <c>sys.server_principals</c> /
    /// <c>sys.database_principals</c> checks to be safe on persistent (LocalSql) runs.
    /// </summary>
    private async Task CreateRlsTestAppLoginAsync()
    {
        // Step 1: create login on master (server-scoped).
        var masterCs = new SqlConnectionStringBuilder(_connectionString!)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        await using var masterConn = new SqlConnection(masterCs);
        await masterConn.OpenAsync();

        const string createLoginSql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{RlsTestLogin}')
                CREATE LOGIN [{RlsTestLogin}]
                WITH PASSWORD = '{RlsTestPassword}',
                     CHECK_EXPIRATION = OFF,
                     CHECK_POLICY = OFF
            """;
        await using (var cmd = new SqlCommand(createLoginSql, masterConn))
            await cmd.ExecuteNonQueryAsync();

        // Step 2: create DB user + grant minimal perms (database-scoped).
        await using var dbConn = new SqlConnection(_connectionString!);
        await dbConn.OpenAsync();

        // Create user if not already present.
        const string createUserSql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{RlsTestLogin}')
                CREATE USER [{RlsTestLogin}] FOR LOGIN [{RlsTestLogin}]
            """;
        await using (var cmd = new SqlCommand(createUserSql, dbConn))
            await cmd.ExecuteNonQueryAsync();

        // Grant minimal table-level permissions.
        // NOT db_owner — RLS predicates skip members of db_owner (IS_MEMBER('db_owner')=1 branch).
        // audit.AuditLogEntries + SecurityEventLogs: covered by asymmetric audit bypass policy.
        // config.SystemParameters: covered by symmetric nullable-string policy.
        const string grantSql = $"""
            GRANT SELECT, INSERT, UPDATE, DELETE
                ON [audit].[AuditLogEntries]            TO [{RlsTestLogin}];
            GRANT SELECT, INSERT, UPDATE, DELETE
                ON [audit].[SecurityEventLogs]          TO [{RlsTestLogin}];
            -- Read-only: AuditEvidenceClientAdminBypassWiringTests drives GetRetentionPolicyStatusAsync,
            -- which reads the retention policy table before its bypass hot-row COUNT.
            GRANT SELECT
                ON [audit].[AuditRetentionPolicies]     TO [{RlsTestLogin}];
            -- Tail-attestation watermark: signer upserts (SELECT for the update-else-insert probe,
            -- INSERT/UPDATE for the write). No DELETE — only the sa connection deletes it (tamper
            -- simulation in the verify-side tamper tests).
            GRANT SELECT, INSERT, UPDATE
                ON [audit].[ChainWatermarks]             TO [{RlsTestLogin}];
            GRANT SELECT, INSERT, UPDATE, DELETE
                ON [config].[SystemParameters]          TO [{RlsTestLogin}];
            GRANT SELECT, INSERT, UPDATE, DELETE
                ON [identity].[SigningDocFlowRecords]   TO [{RlsTestLogin}];
            GRANT SELECT, INSERT, UPDATE, DELETE
                ON [identity].[SigningDocFlowNonces]    TO [{RlsTestLogin}];
            -- forms.FormTemplates carve-out (PUBLIC-FORMS-LEAST-PRIV): the anonymous public read
            -- paths resolve/read templates + child rows under this non-db_owner login. SELECT only
            -- (seeding is done via the sa connection).
            GRANT SELECT
                ON [forms].[FormTemplates]              TO [{RlsTestLogin}];
            GRANT SELECT
                ON [forms].[FormFields]                 TO [{RlsTestLogin}];
            GRANT SELECT
                ON [forms].[FormFieldOptions]           TO [{RlsTestLogin}];
            GRANT SELECT
                ON [forms].[FormTemplateSignerRoles]    TO [{RlsTestLogin}];
            -- forms.FormSubmissionWorkflowLinkPendingAttempts: the retry-job + event-handler writers
            -- run under a non-db_owner login and the enforcement tests probe its FILTER/BLOCK policy.
            -- Needs SELECT+INSERT so the RLS predicates get a chance to fire (error 229 otherwise).
            GRANT SELECT, INSERT, UPDATE, DELETE
                ON [forms].[FormSubmissionWorkflowLinkPendingAttempts] TO [{RlsTestLogin}];
            """;
        await using (var cmd = new SqlCommand(grantSql, dbConn))
            await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Idempotently creates SQL login <c>files_svc_app</c>, a DB user, and grants <c>SELECT ON SCHEMA::[files]</c>
    /// ONLY — no grant on any other schema, no role membership. Models a per-service least-privilege login
    /// (contrast <see cref="CreateRlsTestAppLoginAsync"/>, which spans schemas). Default-deny then makes
    /// <c>SELECT [audit].*</c> fail for this login — that denial is the C3 isolation proof.
    /// </summary>
    private async Task CreateFilesSvcAppLoginAsync()
    {
        // Step 1: create login on master (server-scoped).
        var masterCs = new SqlConnectionStringBuilder(_connectionString!)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        await using var masterConn = new SqlConnection(masterCs);
        await masterConn.OpenAsync();

        const string createLoginSql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{FilesSvcLogin}')
                CREATE LOGIN [{FilesSvcLogin}]
                WITH PASSWORD = '{FilesSvcPassword}',
                     CHECK_EXPIRATION = OFF,
                     CHECK_POLICY = OFF
            """;
        await using (var cmd = new SqlCommand(createLoginSql, masterConn))
            await cmd.ExecuteNonQueryAsync();

        // Step 2: create DB user + single schema-scoped grant (database-scoped).
        await using var dbConn = new SqlConnection(_connectionString!);
        await dbConn.OpenAsync();

        const string createUserSql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{FilesSvcLogin}')
                CREATE USER [{FilesSvcLogin}] FOR LOGIN [{FilesSvcLogin}]
            """;
        await using (var cmd = new SqlCommand(createUserSql, dbConn))
            await cmd.ExecuteNonQueryAsync();

        // The whole point of the spike: ONE schema, schema-scoped GRANT, nothing else.
        // GRANT is idempotent in SQL Server (re-granting is a no-op).
        const string grantSql = $"GRANT SELECT ON SCHEMA::[files] TO [{FilesSvcLogin}];";
        await using (var cmd = new SqlCommand(grantSql, dbConn))
            await cmd.ExecuteNonQueryAsync();
    }

    private DbContextOptions<T> Opts<T>(string schema) where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseSqlServer(_connectionString!, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .EnableServiceProviderCaching(false)
            .Options;

    private static async Task MigrateAsync(DbContext ctx)
    {
        await using (ctx)
        {
            await ctx.Database.MigrateAsync();
        }
    }
}

[CollectionDefinition(CollectionName)]
public sealed class RlsPolicyPresenceCollection : ICollectionFixture<RlsPolicyPresenceFixture>
{
    public const string CollectionName = "RlsPolicyPresence";
}
