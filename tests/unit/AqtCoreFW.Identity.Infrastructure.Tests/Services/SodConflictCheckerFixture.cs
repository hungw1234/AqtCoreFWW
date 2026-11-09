using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Hybrid SQL Server fixture for SodConflictChecker tests.
/// Uses local SQL Server 2022 (dev) or Testcontainers (CI).
/// Creates isolated IdentityDbContext per test with transaction rollback.
/// </summary>
public sealed class SodConflictCheckerFixture : IAsyncLifetime
{
    private const string DbName = "AqtCoreFW_Test_Identity";
    private readonly LocalSqlOptions _opts = LocalSqlOptions.FromEnvironment();
    private readonly Testcontainers.MsSql.MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public SodConflictCheckerFixture()
    {
        if (!_opts.IsEnabled)
        {
            _container = new Testcontainers.MsSql.MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("Gov@Test1234!")
                .Build();
        }
    }

    public async Task InitializeAsync()
    {
        if (_opts.IsEnabled)
        {
            await LocalSqlHelper.EnsureDatabaseExistsAsync(_opts, DbName);
            ConnectionString = LocalSqlHelper.BuildConnectionString(_opts, DbName);
        }
        else
        {
            await _container!.StartAsync();
            var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
                _container.GetConnectionString())
            {
                TrustServerCertificate = true,
                Encrypt = Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional,
            };
            ConnectionString = csb.ConnectionString;
        }

        // Create schema via EF migrations
        await using var ctx = CreateDbContext();

        // Verify we're on SQL Server, not InMemory
        var providerName = ctx.Database.ProviderName;
        if (providerName is null || !providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Expected SqlServer provider but got: {providerName}. InMemory is forbidden for tenant-filter tests.");

        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_opts.IsEnabled)
        {
            // Local SQL path: the database persists across test runs.
            return;
        }

        await _container!.StopAsync();
    }

    /// <summary>Creates a fresh DbContext. Caller disposes.</summary>
    public IdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new IdentityDbContext(options);
    }

    /// <summary>Creates a SodConflictChecker with stubbed dependencies.</summary>
    public SodConflictChecker CreateChecker(
        IdentityDbContext db,
        Guid tenantId,
        Dictionary<Guid, HashSet<string>>? userPermissions = null)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var permSvc = Substitute.For<IEffectivePermissionService>();
        userPermissions ??= new();

        permSvc.GetSummaryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var userId = call.ArgAt<Guid>(0);
                var codes = userPermissions.TryGetValue(userId, out var set)
                    ? set
                    : [];
                return Task.FromResult(new EffectivePermissionSummary
                {
                    UserId = userId,
                    PermissionCodes = codes
                });
            });

        return new SodConflictChecker(db, permSvc, tenantCtx);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    public static SodConflictRule SeedSodRule(
        IdentityDbContext db,
        string codeA,
        string codeB,
        SodSeverity severity,
        bool isActive,
        Guid? tenantId)
    {
        // SodConflictRule is T1: long PK assigned by DB IDENTITY — do NOT set Id.
        var rule = new SodConflictRule
        {
            PermissionCodeA = codeA,
            PermissionCodeB = codeB,
            Severity = severity,
            IsActive = isActive,
            TenantId = tenantId,
            Description = $"Conflict: {codeA} vs {codeB}"
        };
        db.SodConflictRules.Add(rule);
        return rule;
    }

    public static ApplicationRole SeedRole(IdentityDbContext db, Guid roleId, Guid? tenantId)
    {
        var role = new ApplicationRole
        {
            Id = roleId,
            Name = $"Role-{roleId:N}",
            NormalizedName = $"ROLE-{roleId:N}",
            TenantId = tenantId
        };
        db.Roles.Add(role);
        return role;
    }

    public static Permission SeedPermission(IdentityDbContext db, string code)
    {
        var perm = new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            ModuleCode = "TEST",
            ResourceCode = "TEST",
            ActionCode = "TEST"
        };
        db.Permissions.Add(perm);
        return perm;
    }

    public static RolePermission SeedRolePermission(
        IdentityDbContext db,
        Guid roleId,
        Guid permissionId)
    {
        var rp = new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId
        };
        db.RolePermissions.Add(rp);
        return rp;
    }
}

/// <summary>xUnit collection for shared SQL fixture across SoD checker tests.</summary>
[CollectionDefinition(Name)]
public sealed class SodCheckerCollection : ICollectionFixture<SodConflictCheckerFixture>
{
    public const string Name = "SodChecker";
}
