using System.Reflection;
using AqtCoreFW.Infrastructure.ApiKeys;
using AqtCoreFW.Infrastructure.Webhooks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Backstops ADR-009 (webhook/api-key tenant isolation = predicate-only, RLS rejected).
///
/// Invariant: every entity exposed by a shared-infrastructure <see cref="DbContext"/>
/// (assembly <c>AqtCoreFW.Infrastructure</c>) that carries a <c>TenantId</c> property MUST be
/// declared in <see cref="PredicateOnlyAllowlist"/> with a documented reason. This forces a
/// FUTURE tenant-scoped shared-infra table to make a conscious isolation decision instead of
/// silently shipping with neither RLS nor a predicate-only rationale.
///
/// Why a registry/allowlist keyed by entity TYPE, not a column-name heuristic: the shared-infra
/// assembly also hosts system-wide contexts (Alerting/Backup/Messaging) whose tables have NO
/// TenantId — a name heuristic risks false positives there. Reflection over real DbSet&lt;T&gt;
/// entity types is exact.
///
/// Why these tables are NOT given RLS (the thing this guard documents): both surfaces read
/// without an ambient tenant — <c>ApiKeyService.ValidateAsync</c> looks a key up by prefix
/// BEFORE the tenant is known, and <c>WebhookDeliveryJob</c> reads in a Hangfire background job
/// with no tenant context. RLS (predicate keyed on SESSION_CONTEXT('TenantId')) would zero-row
/// both under a least-privilege runtime login → all M2M auth dead + silent webhook-delivery
/// failure. See ADR-009.
/// </summary>
public sealed class SharedInfraTenantTableIsolationGuardTests
{
    /// <summary>
    /// Tenant-scoped shared-infra entities that are isolated by explicit <c>WHERE TenantId</c>
    /// predicate instead of SQL Row-Level Security. Each entry MUST cite the reason RLS is
    /// infeasible (lookup-before-tenant / background read). See ADR-009.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> PredicateOnlyAllowlist =
        new Dictionary<Type, string>
        {
            [typeof(WebhookSubscription)] =
                "Read in Hangfire background delivery job (WebhookDeliveryJob) with no tenant context → " +
                "RLS would zero-row → silent delivery failure. Predicate-only by design. ADR-009.",
            [typeof(ApiKeyRecord)] =
                "Looked up by Prefix BEFORE the tenant is known (ApiKeyService.ValidateAsync, lookup-before-tenant) → " +
                "RLS would reject every key → M2M auth dead. Predicate-only by design. ADR-009.",
            [typeof(ApiKeyScope)] =
                "Child of ApiKeyRecord, loaded on the same lookup-before-tenant access path. ADR-009.",
        };

    [Fact]
    public void EveryTenantScopedSharedInfraEntity_IsPredicateOnlyAllowlisted()
    {
        var unaccounted = FindUnaccounted(DiscoverTenantScopedSharedInfraEntities());

        unaccounted.Should().BeEmpty(
            "a shared-infra table carrying TenantId must either get an RLS policy or be declared " +
            "predicate-only in PredicateOnlyAllowlist with a documented reason + ADR reference. " +
            "Unaccounted tenant-scoped entities: {0}. " +
            "If the table is on an HTTP-request path it likely belongs in a module DbContext (which gets RLS), " +
            "not shared infra; if it is lookup-before-tenant or background-read, add it to the allowlist per ADR-009.",
            string.Join(", ", unaccounted.Select(t => t.FullName)));
    }

    [Fact]
    public void Discovery_FindsKnownTenantScopedEntities()
    {
        // Anti-vacuous-green: if reflection returns an empty/partial set (assembly not loaded,
        // DbSet detection broken), the allowlist check above passes for the WRONG reason.
        // Pin that the discovery actually sees the three known tenant-scoped shared-infra tables.
        var discovered = DiscoverTenantScopedSharedInfraEntities();

        discovered.Should().Contain(typeof(WebhookSubscription));
        discovered.Should().Contain(typeof(ApiKeyRecord));
        discovered.Should().Contain(typeof(ApiKeyScope));
        discovered.Should().HaveCountGreaterThanOrEqualTo(3,
            "discovery must find at least the three known tenant-scoped shared-infra entities");
    }

    [Fact]
    public void Allowlist_HasNoStaleEntries()
    {
        // Keep the allowlist honest: a removed/renamed entity must not leave a dead allowlist
        // entry silently weakening the guard.
        var discovered = DiscoverTenantScopedSharedInfraEntities();

        var stale = PredicateOnlyAllowlist.Keys.Where(t => !discovered.Contains(t)).ToList();

        stale.Should().BeEmpty(
            "allowlist entries must correspond to a live tenant-scoped shared-infra entity. " +
            "Stale entries: {0}", string.Join(", ", stale.Select(t => t.FullName)));
    }

    [Fact]
    public void Guard_RejectsUnknownTenantScopedType()
    {
        // Teeth proof: a brand-new tenant-scoped table that nobody allowlisted MUST be flagged.
        // Exercise the SAME pipeline the main test runs (IsTenantScoped + FindUnaccounted) against
        // a synthetic entity, so a future refactor that neuters either step fails HERE — not only
        // via the out-of-suite manual allowlist-removal check.
        IsTenantScoped(typeof(FakeUntrackedTenantTable))
            .Should().BeTrue("synthetic fixture must be detected as tenant-scoped (Guid TenantId)");

        var flagged = FindUnaccounted(new[] { typeof(FakeUntrackedTenantTable) });

        flagged.Should().ContainSingle()
            .Which.Should().Be(typeof(FakeUntrackedTenantTable),
                "an un-allowlisted tenant-scoped entity must be reported as a violation by FindUnaccounted");
    }

    // ── Allowlist filter (shared by main + teeth tests) ──────────────────────────

    /// <summary>
    /// The actual guard predicate: of the discovered tenant-scoped entities, which are NOT
    /// declared predicate-only. A non-empty result is a violation. Shared by the live-discovery
    /// test and the synthetic teeth test so both exercise the same filter.
    /// </summary>
    private static List<Type> FindUnaccounted(IEnumerable<Type> discovered) =>
        discovered.Where(t => !PredicateOnlyAllowlist.ContainsKey(t)).ToList();

    // ── Discovery ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reflects over every concrete <see cref="DbContext"/> in the shared-infra assembly and
    /// returns the distinct entity types (behind <c>DbSet&lt;T&gt;</c> properties) that carry a
    /// <c>Guid TenantId</c> property.
    ///
    /// Known coverage limits (intentional, no current violation — see ADR-009 §registry):
    ///   • Walks <c>DbSet&lt;T&gt;</c> properties only. An EF owned type or a tenant-scoped entity
    ///     reachable only via navigation (no DbSet) would escape. None exist today.
    ///   • Treats only <c>Guid TenantId</c> as tenant-scoped (<see cref="IsTenantScoped"/>); a
    ///     non-Guid or nullable tenant key would not be flagged. All tenant columns are Guid today.
    /// A future change introducing either shape must extend discovery (e.g. via
    /// <c>context.Model.GetEntityTypes()</c>) rather than rely on this DbSet walk.
    /// </summary>
    private static HashSet<Type> DiscoverTenantScopedSharedInfraEntities()
    {
        var infraAssembly = typeof(WebhookDbContext).Assembly; // AqtCoreFW.Infrastructure

        var contexts = SafeGetTypes(infraAssembly)
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && t is { IsAbstract: false, IsClass: true });

        var result = new HashSet<Type>();
        foreach (var ctx in contexts)
        {
            foreach (var prop in ctx.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.PropertyType.IsGenericType ||
                    prop.PropertyType.GetGenericTypeDefinition() != typeof(DbSet<>))
                    continue;

                var entity = prop.PropertyType.GetGenericArguments()[0];
                if (IsTenantScoped(entity))
                    result.Add(entity);
            }
        }

        return result;
    }

    private static bool IsTenantScoped(Type entity)
    {
        var tenantProp = entity.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
        return tenantProp is not null && tenantProp.PropertyType == typeof(Guid);
    }

    /// <summary>
    /// Tolerant <see cref="Assembly.GetTypes"/> — a partial type-load (e.g. a missing optional
    /// dependency) yields a <see cref="ReflectionTypeLoadException"/> whose <c>Types</c> array
    /// still holds the resolvable types. Mirrors the hardening applied to ModuleStartupWiringTests.
    /// </summary>
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

    /// <summary>Synthetic stand-in for a future tenant-scoped shared-infra table (teeth test only).</summary>
    private sealed class FakeUntrackedTenantTable
    {
        public Guid Id { get; init; }
        public Guid TenantId { get; init; }
    }
}
