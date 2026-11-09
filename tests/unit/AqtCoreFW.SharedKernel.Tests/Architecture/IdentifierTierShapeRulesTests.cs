using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace AqtCoreFW.SharedKernel.Tests.Architecture;

/// <summary>
/// IL-level shape rules — operate on compiled SharedKernel + Infrastructure assemblies.
/// Phase-3 (2026-05-03) MVP scope: foundation assemblies only (modules broken until Phase-4a).
/// Phase-4a per-batch will revive `tests/architecture/AqtCoreFW.Architecture.Tests/` with module-wide IL rules.
///
/// Rules:
///   R-Shape-1: IPublicIdentified implementers have Guid PublicId property
///   R-Shape-2: IMessageIdentified implementers have Guid MessageId property
///   R-Shape-3: Entity has long Id property
///   R-Shape-4: AuditableEntity preserves Guid CreatedBy/ModifiedBy (FK exception list — references AspNetUsers.Id)
///   R-Shape-6: T1 entities have long Id, no IPublicIdentified
///   R-Shape-7: T2 entities have long Id + Guid PublicId via IPublicIdentified
///   R-Shape-8: T3 entities have Guid MessageId (IMessageIdentified or property); PK = long InternalId (plain-class) or long Id (Entity-derived)
/// </summary>
[Trait("Category", "Architecture")]
public sealed class IdentifierTierShapeRulesTests
{
    private static readonly Assembly[] FoundationAssemblies =
    [
        typeof(Entity).Assembly,
        typeof(AqtCoreFW.Infrastructure.Configuration.EntityTypeConfigurationBase<>).Assembly,
    ];

    /// <summary>
    /// ASP.NET Identity framework entities — Guid PK locked by OpenIddict (cannot migrate to long).
    /// Per memory: phase-04b-b2-shipped.md — ApplicationUser + ApplicationRole preserved Guid PK.
    /// ApplicationUser is NOT in the CSV tier registry; ApplicationRole is CSV T2 (exception applies to Rule7).
    ///
    /// Satellite junction/lookup entities adjacent to the ASP.NET Identity Guid-PK cluster:
    ///   - GroupRoleAssignment: FK to ApplicationRole (Guid) + UserGroup (long) — mixed, Guid standalone PK
    ///   - RoleDataScope: FK to ApplicationRole via RoleId (Guid)
    ///   - RolePermission: composite PK (RoleId Guid, PermissionId Guid) — no standalone Id
    ///   - MenuRolePermission: composite PK (MenuId long, PermissionCode string) — no standalone Id
    ///   - Permission: Guid Id (RBAC leaf, predates Phase-04 refactor; Code is natural key)
    ///   - UserGroupMembership: FK to ApplicationUser (Guid) — Guid standalone PK
    ///
    /// Retier recommendation for planner: these 6 identity satellite entities need a
    /// follow-up plan to either (a) migrate Guid PK → long where FK allows, or
    /// (b) classify as a permanent "ASP.NET Identity cluster" exception tier.
    /// Tracked: Phase-04c Rule6 finding, 2026-05-04.
    /// </summary>
    private static readonly HashSet<string> AspNetIdentityGuidPkExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core ASP.NET Identity framework entities (OpenIddict lock)
        "ApplicationUser",
        "ApplicationRole",
        // Satellite junction/lookup entities — FK-constrained to Guid-PK Identity entities
        "GroupRoleAssignment",
        "RoleDataScope",
        "RolePermission",
        "MenuRolePermission",
        "Permission",
        "UserGroupMembership",
    };

    [Fact]
    public void Entity_Has_Long_Id_Property()
    {
        var idProperty = typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance);
        idProperty.Should().NotBeNull();
        idProperty!.PropertyType.Should().Be<long>("Phase-2 Foundation Refactor: Id is bigint (long); was Guid generic prior.");
    }

    [Fact]
    public void Entity_CreatedAt_Is_DateTime_Not_DateTimeOffset()
    {
        var prop = typeof(Entity).GetProperty(nameof(Entity.CreatedAt));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be<DateTime>("Phase-2 H1 amendment: datetime2(3) universal; DateTimeOffset banned.");
    }

    [Fact]
    public void Entity_UpdatedAt_Is_NullableDateTime()
    {
        var prop = typeof(Entity).GetProperty(nameof(Entity.UpdatedAt));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be<DateTime?>();
    }

    [Fact]
    public void AuditableEntity_CreatedBy_Is_Guid()
    {
        // FK exception (Phase-0 fk-guid-exception-list): CreatedBy/ModifiedBy reference AspNetUsers.Id (Guid framework PK).
        var prop = typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.CreatedBy));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be<Guid>();
    }

    [Fact]
    public void AuditableEntity_ModifiedBy_Is_NullableGuid()
    {
        var prop = typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.ModifiedBy));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be<Guid?>();
    }

    [Fact]
    public void IPublicIdentified_Implementers_HaveGuid_PublicId_Property()
    {
        var implementers = ScanImplementersOf(typeof(IPublicIdentified));
        // Phase-3 MVP: foundation assemblies have ZERO concrete IPublicIdentified implementers (Tenant.PublicId added Phase-4a).
        // Rule structure proven; once Phase-4a starts adding implementers, this fires.
        foreach (var t in implementers)
        {
            var prop = t.GetProperty(nameof(IPublicIdentified.PublicId));
            prop.Should().NotBeNull($"{t.FullName} implements IPublicIdentified but lacks PublicId property");
            prop!.PropertyType.Should().Be<Guid>();
        }
    }

    [Fact]
    public void IMessageIdentified_Implementers_HaveGuid_MessageId_Property()
    {
        var implementers = ScanImplementersOf(typeof(IMessageIdentified));
        foreach (var t in implementers)
        {
            var prop = t.GetProperty(nameof(IMessageIdentified.MessageId));
            prop.Should().NotBeNull($"{t.FullName} implements IMessageIdentified but lacks MessageId property");
            prop!.PropertyType.Should().Be<Guid>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R6/R7/R8 — Tier registry shape rules (Phase-04c, 2026-05-04)
    // Source: plans/260503-0045-foundation-perf-surgery/research/entity-tier-mapping-data.csv
    // Assembly loading: TierShapeAssertionHelpers probes disk (module DLLs not project-referenced).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sanity: verify that at least one module Domain assembly can be loaded from disk.
    /// If this fails, R6/R7/R8 will silently skip all entities (no coverage). Fix assembly probe first.
    /// </summary>
    [Fact]
    public void Sanity_ModuleAssembliesAreLoadable_For_Tier_Rules()
    {
        // Tenant is a well-known T2 entity in identity module (Phase-04a shipped).
        var tenantType = TierShapeAssertionHelpers.FindEntityType("identity", "Tenant");
        tenantType.Should().NotBeNull(
            "Phase-04a shipped identity.Tenant as T2; if null the module Domain DLL is not " +
            "in the build output bin folder — R6/R7/R8 would be no-ops. " +
            "Ensure dotnet build has been run for the full solution before running these tests.");
    }

    /// <summary>
    /// Rule6: T1 entities have long Id (inherited from Entity base) and do NOT implement IPublicIdentified.
    /// A T1 appearing in the violation list means it was accidentally given a PublicId — must retier to T2 or remove interface.
    /// Skips entities whose Type cannot be resolved (Phase-08 coverage fact will catch gaps).
    /// </summary>
    [Fact]
    public void Rule6_T1_Entities_Have_Long_Id_And_No_PublicId()
    {
        var registry = T2EntityRegistry.Load();
        var violations = new List<string>();

        foreach (var entry in registry.T1())
        {
            if (AspNetIdentityGuidPkExceptions.Contains(entry.Entity)) continue;

            var t = TierShapeAssertionHelpers.FindEntityType(entry.Module, entry.Entity);
            if (t is null) continue; // DLL not built yet or entity deferred — Phase-08 coverage fact

            // T1 must inherit Entity (which has long Id). Check the Id property type.
            var idProp = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (idProp is null || idProp.PropertyType != typeof(long))
                violations.Add($"{entry.Module}.{entry.Entity} — T1 expected long Id, got {idProp?.PropertyType.Name ?? "<missing>"}");

            // T1 must NOT implement IPublicIdentified (that's the T2 contract).
            if (typeof(IPublicIdentified).IsAssignableFrom(t))
                violations.Add($"{entry.Module}.{entry.Entity} — T1 must NOT implement IPublicIdentified (retier to T2 if API-exposed)");
        }

        violations.Should().BeEmpty(
            "T1 contract: Entity<long> with no IPublicIdentified. Violations:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Rule7: T2 entities have long Id (Entity base) AND implement IPublicIdentified with a Guid PublicId.
    /// A T2 appearing in the violation list means the Phase-04 refactor is incomplete for that entity.
    /// </summary>
    [Fact]
    public void Rule7_T2_Entities_Have_Long_Id_And_Guid_PublicId()
    {
        var registry = T2EntityRegistry.Load();
        var violations = new List<string>();

        foreach (var entry in registry.T2())
        {
            if (AspNetIdentityGuidPkExceptions.Contains(entry.Entity)) continue;

            var t = TierShapeAssertionHelpers.FindEntityType(entry.Module, entry.Entity);
            if (t is null) continue;

            // T2 must have long Id (via Entity base).
            var idProp = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (idProp is null || idProp.PropertyType != typeof(long))
                violations.Add($"{entry.Module}.{entry.Entity} — T2 expected long Id, got {idProp?.PropertyType.Name ?? "<missing>"}");

            // T2 must implement IPublicIdentified.
            if (!typeof(IPublicIdentified).IsAssignableFrom(t))
            {
                violations.Add($"{entry.Module}.{entry.Entity} — T2 must implement IPublicIdentified");
            }
            else
            {
                // PublicId property must be Guid.
                var pubProp = t.GetProperty("PublicId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (pubProp is null || pubProp.PropertyType != typeof(Guid))
                    violations.Add($"{entry.Module}.{entry.Entity} — T2 PublicId must be Guid, got {pubProp?.PropertyType.Name ?? "<missing>"}");
            }
        }

        violations.Should().BeEmpty(
            "T2 contract: Entity<long> + IPublicIdentified (Guid PublicId). Violations:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Rule8: T3 entities expose a Guid MessageId — either via IMessageIdentified interface or as a named property.
    /// T3 plain-class entities use long InternalId as PK (not Id); Entity-derived T3 use long Id.
    /// Both variants are accepted here — the critical invariant is the Guid MessageId AK.
    /// Skips EF AlternateKey check (Rule8b deferred per plan).
    /// </summary>
    [Fact]
    public void Rule8_T3_Entities_Have_Guid_MessageId()
    {
        var registry = T2EntityRegistry.Load();
        var violations = new List<string>();

        foreach (var entry in registry.T3())
        {
            var t = TierShapeAssertionHelpers.FindEntityType(entry.Module, entry.Entity);
            if (t is null) continue;

            // T3 entities come in two PK shapes:
            //   a) Plain-class: long InternalId (not derived from Entity)
            //   b) Entity-derived: long Id (via Entity base) — less common but permitted
            // Check that the type has either a long InternalId or a long Id (not Guid).
            var internalIdProp = t.GetProperty("InternalId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            var idProp = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            var hasLongPk = (internalIdProp?.PropertyType == typeof(long))
                         || (idProp?.PropertyType == typeof(long));
            if (!hasLongPk)
                violations.Add($"{entry.Module}.{entry.Entity} — T3 must have long InternalId (plain-class) or long Id (Entity-derived), found neither");

            // The critical T3 invariant: must expose Guid MessageId.
            var hasInterface = typeof(IMessageIdentified).IsAssignableFrom(t);
            if (!hasInterface)
            {
                // Fallback: may have property without interface (legacy/transitional).
                var msgProp = t.GetProperty("MessageId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (msgProp is null || msgProp.PropertyType != typeof(Guid))
                    violations.Add($"{entry.Module}.{entry.Entity} — T3 must expose Guid MessageId via IMessageIdentified or property");
            }
        }

        violations.Should().BeEmpty(
            "T3 contract: long PK (InternalId or Id) + Guid MessageId. Violations:\n  " +
            string.Join("\n  ", violations));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Foundation scanner helpers (pre-existing)
    // ─────────────────────────────────────────────────────────────────────────

    private static IEnumerable<Type> ScanImplementersOf(Type iface) =>
        FoundationAssemblies
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t.IsClass && !t.IsAbstract && iface.IsAssignableFrom(t));

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
