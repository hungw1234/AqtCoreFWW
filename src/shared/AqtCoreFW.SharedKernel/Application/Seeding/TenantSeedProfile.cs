namespace AqtCoreFW.SharedKernel.Application.Seeding;

/// <summary>
/// Tenant-agnostic seed configuration consumed by the tenant, org-unit and identity seeders.
///
/// Pure data — deliberately carries NO module-specific behaviour (e.g. no org-tree builder
/// delegate). The Organization module owns its tree-building strategy and keys it off
/// <see cref="Code"/> / <see cref="TenantId"/>; the Identity module derives roles/users from the
/// prefix + count fields. Living in SharedKernel lets both Identity and Organization reference one
/// canonical contract without a cross-module dependency (the modules cannot reference each other).
///
/// <see cref="TenantId"/> is the <c>Tenant.PublicId</c> value — the Guid that every cross-module FK
/// (AspNetUsers.TenantId, OrgUnits.TenantId) targets. It is supplied here (not generated) so the
/// seeded rows line up deterministically across modules and across reseeds.
/// </summary>
public sealed record TenantSeedProfile
{
    /// <summary>Deterministic tenant identity = Tenant.PublicId. Cross-module FK target.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Stable tenant code (e.g. "BoTaiChinh", "BoNoiVu"). Immutable once a row exists.</summary>
    public required string Code { get; init; }

    /// <summary>Display name with full Vietnamese diacritics (e.g. "Bộ Tài chính", "Bộ Nội Vụ").</summary>
    public required string Name { get; init; }

    /// <summary>Optional administrative contact email for the tenant row.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Role code prefix for this tenant's business roles (e.g. "BTC_", "BNV_").</summary>
    public required string RolePrefix { get; init; }

    /// <summary>Prefix for generated seed-user codes (e.g. "BTC-SEED-").</summary>
    public required string UserCodePrefix { get; init; }

    /// <summary>Email local-part prefix for the one lead user per org unit (e.g. "btc.lead.").</summary>
    public required string LeadEmailPrefix { get; init; }

    /// <summary>Email local-part prefix for reserved dev users (e.g. "btc.dev.").</summary>
    public required string DevEmailPrefix { get; init; }

    /// <summary>Email local-part prefix for weight-fill users (e.g. "btc").</summary>
    public required string FillEmailPrefix { get; init; }

    /// <summary>Email domain for all generated users (e.g. "@mof.test", "@moha.test").</summary>
    public required string EmailDomain { get; init; }

    /// <summary>Target total user count for this tenant (lead + fill + dev reserve).</summary>
    public required int UserCount { get; init; }

    /// <summary>Maximum org-tree depth to seed for this tenant (BTC = 4 levels, BNV = 2).</summary>
    public required int MaxLevel { get; init; }

    /// <summary>Provenance stamp for the seed dataset (e.g. "v1-2026-04").</summary>
    public required string SeedVersion { get; init; }
}
