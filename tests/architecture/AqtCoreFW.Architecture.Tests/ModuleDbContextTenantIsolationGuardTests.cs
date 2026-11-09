using System.Reflection;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

// DbContext anchor types — one per module Infrastructure assembly.
// Used only to locate the assembly; no methods called.
using AqtCoreFW.Ai.Infrastructure.Persistence;
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
using AqtCoreFW.Workflow.Infrastructure.Persistence;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Backstops module-level tenant isolation: every <see cref="ITenantScoped"/> entity mapped on a
/// module <see cref="DbContext"/> MUST either have an RLS policy in a migration file OR be declared in
/// <see cref="PredicateOnlyAllowlist"/> with a documented reason.
///
/// <para>This guard complements <see cref="SharedInfraTenantTableIsolationGuardTests"/> (which covers
/// the shared-infrastructure DbContext). The shared-infra guard is predicate-only by design (ADR-009);
/// module DbContexts are request-scoped and SHOULD have RLS.</para>
///
/// <para>DISCOVERY: each module DbContext is instantiated (with a stub <see cref="ITenantContext"/> /
/// <see cref="IKeyResolver"/> and a bogus SQL Server connection string — no DB is opened) and its
/// compiled relational model is read via <c>Model.GetEntityTypes()</c>. This covers entities mapped via
/// ApplyConfigurationsFromAssembly that have NO DbSet (e.g. TenantSignatureConfig), which a DbSet&lt;T&gt;
/// property walk would miss. Table names come from the relational model (<c>GetTableName()</c>), not a
/// hand-rolled pluralization heuristic. Residual: an entity mapped inline in OnModelCreating without an
/// IEntityTypeConfiguration class is still discovered (it is in the model) — the only true blind spot is
/// a context that fails to instantiate, which the anti-vacuous count test surfaces (skipped contexts are
/// listed in its failure message).</para>
///
/// <para>RLS detection: scans all non-Designer migration source files for the entity's table name
/// appearing in any of the three RlsMigrationHelper variant calls or a raw CREATE SECURITY POLICY
/// statement. Detection works on the source tree located by walking up from the test binary.</para>
///
/// <para>Allowlist rationale (predicate-only by documented reason):</para>
/// <list type="bullet">
///   <item><term>Workflow definition-layer tables</term><description>
///     DelegationRules, WorkflowAssignmentRules, WorkflowNotificationConfigs, VersionMigrations —
///     tenant-configuration tables read by background evaluators across ALL tenants without ambient
///     tenant context (EscalationEvaluator, DelegationResolver etc). Adding RLS would zero-row
///     every cross-tenant sweep → silent evaluation failure. Isolation enforced by explicit
///     WHERE TenantId predicate + query filters.</description></item>
///   <item><term>FormSubmissionWorkflowLinkPendingAttempts</term><description>
///     ADR-009 extension. Auto-retry background job (WorkflowLinkRetryJob) processes ALL pending
///     attempts across tenants without ambient tenant context. RLS would zero-row the retry scan →
///     silent retry failure (permanent orphaned submissions). Same pattern as WebhookDeliveryJob
///     in ADR-009.</description></item>
/// </list>
/// </summary>
public sealed class ModuleDbContextTenantIsolationGuardTests
{
    // ── Allowlist — predicate-only entities (not RLS-protected) ─────────────────────────────────
    //
    // Each entry: entity type → reason string.
    // Entries MUST have a documented reason. Add only after coordinator approval.

    private static readonly IReadOnlyDictionary<Type, string> PredicateOnlyAllowlist =
        new Dictionary<Type, string>
        {
            // Workflow definition-layer — config entities evaluated across all tenants by background services.
            [typeof(AqtCoreFW.Workflow.Domain.Entities.DelegationRule)] =
                "Workflow definition-layer: read by background delegation evaluator across ALL tenants " +
                "without ambient tenant context. RLS would zero-row cross-tenant sweeps. Predicate-only.",
            [typeof(AqtCoreFW.Workflow.Domain.Entities.WorkflowAssignmentRule)] =
                "Workflow definition-layer: read by background assignment evaluator across ALL tenants. " +
                "RLS would zero-row cross-tenant sweeps. Predicate-only.",
            [typeof(AqtCoreFW.Workflow.Domain.Entities.WorkflowNotificationConfig)] =
                "Workflow definition-layer: notification configuration read across ALL tenants by " +
                "background notification job. RLS would zero-row cross-tenant reads. Predicate-only.",
            [typeof(AqtCoreFW.Workflow.Domain.Entities.WorkflowVersionMigration)] =
                "Workflow definition-layer: version-migration records processed across ALL tenants by " +
                "migration runner. Table name is VersionMigrations (non-convention). Predicate-only.",

            // Forms retry queue — ADR-009 extension.
            [typeof(AqtCoreFW.Forms.Domain.Entities.FormSubmissionWorkflowLinkPendingAttempt)] =
                "ADR-009 extension: WorkflowLinkRetryJob processes ALL pending attempts across tenants " +
                "without ambient tenant context → RLS would zero-row the retry scan, causing silent " +
                "permanent orphaned submissions. Same pattern as WebhookDeliveryJob (ADR-009). Predicate-only.",

            // Identity per-tenant signing config — ADR-009 class (cross-tenant startup reader).
            [typeof(AqtCoreFW.Identity.Domain.Entities.TenantSignatureConfig)] =
                "IdentityDbContext does NOT derive ModuleDbContext and applies no tenant query filter; " +
                "SigningHealthCheckService (startup IHostedService) counts enabled configs across ALL " +
                "tenants with no ambient tenant context → RLS would zero-row that check, silently failing " +
                "the root-CA-store startup health warning (same class as WebhookDeliveryJob, ADR-009). " +
                "Request-scoped readers (GetSigningProviderInfoQuery, SignPayloadCommand, Verify* commands) " +
                "isolate via an explicit WHERE TenantId predicate. Predicate-only.",
        };

    // ── Assemblies hosting module DbContexts ────────────────────────────────────────────────────
    //
    // Each anchor type's assembly is scanned for concrete DbContext subclasses.
    // The anchor need not be a DbContext itself — it just pins the assembly reference.

    private static readonly Assembly[] ModuleInfraAssemblies =
    [
        typeof(AiDbContext).Assembly,
        typeof(BusinessProcessDbContext).Assembly,
        typeof(CollaborationDbContext).Assembly,
        typeof(FilesDbContext).Assembly,
        typeof(FormsDbContext).Assembly,
        typeof(IdentityDbContext).Assembly,
        typeof(IntegrationDbContext).Assembly,
        typeof(MasterDataDbContext).Assembly,
        typeof(NotificationsDbContext).Assembly,
        typeof(OrgDbContext).Assembly,
        typeof(ReportingDbContext).Assembly,
        typeof(RulesDbContext).Assembly,
        typeof(WorkflowDbContext).Assembly,
    ];

    // ── Main guard test ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryModuleTenantScopedEntity_HasRls_OrIsAllowlisted()
    {
        var repoRoot = LocateRepoRoot();
        var migrationContents = LoadMigrationContents(repoRoot);

        var entityTables = TenantEntityTables;
        var violations = new List<string>();

        foreach (var (entity, tableName) in entityTables)
        {
            if (PredicateOnlyAllowlist.ContainsKey(entity)) continue;

            if (!IsRlsCovered(tableName, migrationContents))
            {
                violations.Add(
                    $"{entity.FullName} → table \"{tableName}\" — no RLS policy found in any " +
                    $"migration file and not in PredicateOnlyAllowlist. Either add RLS (via " +
                    $"RlsMigrationHelper.GenerateRlsPolicyStatements) or add to allowlist with reason.");
            }
        }

        violations.Should().BeEmpty(
            "every module ITenantScoped entity must have RLS or a documented predicate-only allowlist entry.\n" +
            "Violations:\n" + string.Join("\n", violations));
    }

    // ── Anti-vacuous: discovery finds expected minimum ───────────────────────────────────────────

    [Fact]
    public void Discovery_FindsExpectedMinimumTenantScopedEntities()
    {
        var discovered = DiscoverTenantScopedModuleEntities();

        // Sanity: at least 30 tenant-scoped module entities exist as of this writing.
        // If discovery breaks (assembly not loaded, DbSet walk fails), this catches it before
        // the main test passes vacuously.
        discovered.Should().HaveCountGreaterThanOrEqualTo(30,
            "discovery must find at least 30 ITenantScoped module entities across all module DbContexts; " +
            $"found {discovered.Count}. Check that ModuleInfraAssemblies is complete and every module " +
            "DbContext instantiates for model-read. Skipped contexts (if any):\n" +
            (s_skippedContexts.Count == 0 ? "  (none)" : string.Join("\n", s_skippedContexts)));

        // Spot-check: known entities from specific modules must be discovered.
        discovered.Should().Contain(typeof(AqtCoreFW.Identity.Domain.Entities.SignerCertEnrollment),
            "identity module ITenantScoped entity must be discovered");
        discovered.Should().Contain(typeof(AqtCoreFW.Forms.Domain.Entities.FormSubmission),
            "forms module ITenantScoped entity must be discovered");
        discovered.Should().Contain(typeof(AqtCoreFW.Workflow.Domain.Entities.WorkflowTask),
            "workflow module ITenantScoped entity must be discovered");

        // Config-only entity (mapped via IEntityTypeConfiguration, NO DbSet) must be discovered —
        // proves the model-based discovery closes the DbSet-walk blind spot.
        discovered.Should().Contain(typeof(AqtCoreFW.Identity.Domain.Entities.TenantSignatureConfig),
            "config-only ITenantScoped entity (no DbSet) must be discovered via Model.GetEntityTypes()");
    }

    // ── Allowlist stale-entry guard ──────────────────────────────────────────────────────────────

    [Fact]
    public void Allowlist_HasNoStaleEntries()
    {
        var discovered = DiscoverTenantScopedModuleEntities();

        var stale = PredicateOnlyAllowlist.Keys
            .Where(t => !discovered.Contains(t))
            .ToList();

        stale.Should().BeEmpty(
            "allowlist entries must correspond to a live ITenantScoped module entity. " +
            "Stale entries (renamed/removed types must be removed from the allowlist): {0}",
            string.Join(", ", stale.Select(t => t.FullName)));
    }

    // ── Teeth test — un-allowlisted entity is flagged ────────────────────────────────────────────

    [Fact]
    public void Guard_RejectsEntityWithoutRlsOrAllowlist()
    {
        // Exercises the same FindUnaccountedOrUnprotected pipeline as the main test with a synthetic
        // entity that has no RLS and is not in the allowlist — proves the guard would catch a real
        // missing-RLS addition.
        var syntheticDiscovered = new[] { typeof(FakeUnprotectedTenantEntity) };
        var repoRoot = LocateRepoRoot();
        var migrationContents = LoadMigrationContents(repoRoot);

        // Verify the synthetic entity is detected as tenant-scoped.
        IsTenantScoped(typeof(FakeUnprotectedTenantEntity))
            .Should().BeTrue("synthetic fixture must satisfy ITenantScoped detection");

        // Verify the synthetic entity is NOT covered by RLS (table name won't appear in any migration).
        IsRlsCovered("FakeUnprotectedTenantEntities", migrationContents)
            .Should().BeFalse("a fake table name must not appear in any real migration file");

        // Run the same filter used in the main test — must flag the synthetic entity.
        // The synthetic has no real table; its fake table name appears in no migration → not RLS-covered.
        var violations = syntheticDiscovered
            .Where(e => !PredicateOnlyAllowlist.ContainsKey(e)
                        && !IsRlsCovered("FakeUnprotectedTenantEntities", migrationContents))
            .ToList();

        violations.Should().ContainSingle()
            .Which.Should().Be(typeof(FakeUnprotectedTenantEntity),
                "an un-allowlisted ITenantScoped entity without RLS must be reported as a violation");
    }

    // ── Discovery ────────────────────────────────────────────────────────────────────────────────

    // Contexts that could not be instantiated/model-read during discovery — surfaced by the
    // anti-vacuous count test so an undercount never passes silently as "all covered".
    private static readonly List<string> s_skippedContexts = new();

    // Built once: maps every ITenantScoped module entity (with a non-nullable Guid TenantId) to its
    // relational table name, read from each module DbContext's compiled model.
    private static readonly Lazy<IReadOnlyDictionary<Type, string>> s_tenantEntityTables =
        new(BuildTenantEntityTableMap);

    private static IReadOnlyDictionary<Type, string> TenantEntityTables => s_tenantEntityTables.Value;

    /// <summary>
    /// Returns the set of ITenantScoped module entity types discovered across all module DbContexts.
    /// </summary>
    private static HashSet<Type> DiscoverTenantScopedModuleEntities() => TenantEntityTables.Keys.ToHashSet();

    /// <summary>
    /// Instantiates every concrete module DbContext (stub ITenantContext/IKeyResolver + bogus SQL Server
    /// connection — no DB opened) and reads its compiled relational model. Returns every
    /// <see cref="ITenantScoped"/> entity (non-nullable <c>Guid TenantId</c>) → its <c>GetTableName()</c>.
    /// Covers config-only entities with no DbSet that a property walk would miss.
    /// </summary>
    private static IReadOnlyDictionary<Type, string> BuildTenantEntityTableMap()
    {
        var map = new Dictionary<Type, string>();

        foreach (var asm in ModuleInfraAssemblies)
        {
            var contexts = SafeGetTypes(asm)
                .Where(t => typeof(DbContext).IsAssignableFrom(t)
                            && t is { IsAbstract: false, IsClass: true });

            foreach (var ctxType in contexts)
            {
                DbContext? ctx = null;
                try
                {
                    ctx = TryInstantiate(ctxType);
                    if (ctx is null)
                    {
                        s_skippedContexts.Add($"{ctxType.FullName}: no constructor satisfiable with " +
                                              "(DbContextOptions, ITenantContext, IKeyResolver) stubs");
                        continue;
                    }

                    foreach (var entityType in ctx.Model.GetEntityTypes())
                    {
                        var clrType = entityType.ClrType;
                        if (!IsTenantScoped(clrType)) continue;

                        var tableName = entityType.GetTableName();
                        if (tableName is null) continue; // keyless/owned/view — no standalone table

                        map[clrType] = tableName;
                    }
                }
                catch (Exception ex)
                {
                    s_skippedContexts.Add($"{ctxType.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    ctx?.Dispose();
                }
            }
        }

        return map;
    }

    // ── Reflective DbContext instantiation (model-read only) ─────────────────────────────────────────

    private static readonly ITenantContext s_stubTenantContext = new StubTenantContext();
    private static readonly IKeyResolver s_stubKeyResolver = new StubKeyResolver();

    /// <summary>
    /// Instantiates a DbContext for model inspection. Picks the public ctor with the most parameters that
    /// are all satisfiable from (DbContextOptions&lt;ctx&gt;, ITenantContext, IKeyResolver) stubs; returns
    /// null if none qualifies. The tenant filter bakes the stub via Expression.Constant but is never
    /// executed (we only read the model), so the stub's values are irrelevant.
    /// </summary>
    private static DbContext? TryInstantiate(Type ctxType)
    {
        foreach (var ctor in ctxType.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            var satisfiable = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                var pt = parameters[i].ParameterType;
                if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
                    args[i] = BuildOptions(ctxType);
                else if (pt == typeof(ITenantContext))
                    args[i] = s_stubTenantContext;
                else if (pt == typeof(IKeyResolver))
                    args[i] = s_stubKeyResolver;
                else { satisfiable = false; break; }
            }

            if (satisfiable)
                return (DbContext)ctor.Invoke(args);
        }

        return null;
    }

    /// <summary>
    /// Builds a typed <c>DbContextOptions&lt;ctxType&gt;</c> with the SQL Server provider so the compiled
    /// model exposes real relational table names. The connection string is never used (no DB is opened).
    /// </summary>
    private static object BuildOptions(Type ctxType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(ctxType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseSqlServer("Server=localhost;Database=ArchTestModelOnly;Trusted_Connection=False;TrustServerCertificate=True");

        // DbContextOptionsBuilder<T> shadows the base Options property (returns the typed
        // DbContextOptions<T> the ctor needs) — DeclaredOnly avoids an AmbiguousMatchException.
        var optionsProp = builderType.GetProperty(
            "Options", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        return optionsProp.GetValue(builder)!;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => false;
    }

    private sealed class StubKeyResolver : IKeyResolver
    {
        public int CurrentVersion => 1;
        public byte[] GetKey(int version) => new byte[32];
    }

    private static bool IsTenantScoped(Type entity)
    {
        // Must implement ITenantScoped AND have a non-nullable Guid TenantId property.
        if (!typeof(ITenantScoped).IsAssignableFrom(entity)) return false;
        var prop = entity.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
        return prop is not null && prop.PropertyType == typeof(Guid);
    }

    // ── RLS detection ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the text of all non-Designer migration source files from all module migration folders.
    /// </summary>
    private static string[] LoadMigrationContents(string repoRoot)
    {
        var modulesDir = Path.Combine(repoRoot, "src", "modules");

        return Directory.EnumerateFiles(modulesDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains("Migrations") && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();
    }

    // Patterns that indicate RLS coverage for a given table name.
    // Matches the three RlsMigrationHelper variants + raw CREATE SECURITY POLICY.
    //
    // RlsMigrationHelper emits, per table: a predicate function fn_{schema}_{table}_TenantFilter and a
    // security policy pol_{schema}_{table}_Tenant. Detection keys on those exact suffixes so it cannot
    // be tricked by an unrelated identifier that merely starts the same way — notably the conventional
    // unique index UQ_{table}_TenantId, whose "_{table}_Tenant" prefix previously produced a FALSE
    // POSITIVE (a table with that index looked RLS-covered when it was not).
    private static bool IsRlsCovered(string tableName, string[] migrationContents)
    {
        // Two detection strategies:
        //   1. File contains the quoted table name AND an RLS helper method call
        //      (handles multi-line: helper on line N, table name on line N+1).
        //   2. File contains an exact RLS artifact name for this table: the predicate-function suffix
        //      "_{table}_TenantFilter" or the bracket-terminated policy name "_{table}_Tenant]".
        //      The trailing "Filter" / "]" boundary excludes the "_{table}_TenantId" index false match.
        var quotedName = $"\"{tableName}\"";
        var functionFragment = $"_{tableName}_TenantFilter";
        var policyFragment = $"_{tableName}_Tenant]";

        foreach (var content in migrationContents)
        {
            // Strategy 1: file contains quoted table name + any RLS helper call
            if (content.Contains(quotedName) && FileHasRlsHelperCall(content))
                return true;

            // Strategy 2: exact RLS function/policy name for this table (raw or generated DDL)
            if (content.Contains(functionFragment) || content.Contains(policyFragment))
                return true;
        }

        return false;
    }

    // Returns true if the migration file contains any RLS helper method or raw CREATE SECURITY POLICY.
    private static bool FileHasRlsHelperCall(string content) =>
        content.Contains("GenerateRlsPolicyStatements") ||
        content.Contains("GenerateAuditAdminBypassRlsPolicyStatements") ||
        content.Contains("GenerateSystemParamRlsPolicyStatements") ||
        content.Contains("CREATE SECURITY POLICY");

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root (parent of src/) from {AppContext.BaseDirectory} walking up.");
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    // ── Synthetic teeth fixture ───────────────────────────────────────────────────────────────────

    /// <summary>Fake ITenantScoped entity used only by the teeth test — never in a real DbContext.</summary>
    private sealed class FakeUnprotectedTenantEntity : ITenantScoped
    {
        public Guid TenantId { get; init; }
    }
}
