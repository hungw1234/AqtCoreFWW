namespace AqtCoreFW.SharedKernel.Application.Seeding;

/// <summary>
/// Canonical set of <see cref="TenantSeedProfile"/> instances for demo/test seeding.
///
/// This is the single source of truth for the per-tenant identity constants that were previously
/// duplicated as literals across module-local seed-constant classes (Organization + Identity could
/// not share them). The deterministic GUIDs are preserved verbatim so existing seeded data and FK
/// references stay valid.
///
/// GUID convention: suffix hex-encodes a mnemonic — "b7c1" → BTC tenant #1, "b7c2" → the second
/// tenant (Bộ Nội Vụ). Neither collides with the dev tenant ("…0001").
/// </summary>
public static class TenantSeedProfiles
{
    /// <summary>Bộ Tài chính — the primary reference tenant (~1139 org units / ~10k users).</summary>
    public static readonly TenantSeedProfile BoTaiChinh = new()
    {
        TenantId = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000b7c1"),
        Code = "BoTaiChinh",
        Name = "Bộ Tài chính",
        ContactEmail = "admin@mof.test",
        RolePrefix = "BTC_",
        UserCodePrefix = "BTC-SEED-",
        LeadEmailPrefix = "btc.lead.",
        DevEmailPrefix = "btc.dev.",
        FillEmailPrefix = "btc",
        EmailDomain = "@mof.test",
        UserCount = 10_000,
        MaxLevel = 4,
        SeedVersion = "v1-2026-04",
    };

    /// <summary>
    /// Bộ Nội Vụ — second tenant, post-merger structure (Nghị định 25/2025): 22 second-level units
    /// (10 Vụ + 5 Cục + 3 Ban/Thanh tra/Văn phòng + 4 sự nghiệp), ~1.5k users. 2-level org tree.
    /// </summary>
    public static readonly TenantSeedProfile BoNoiVu = new()
    {
        TenantId = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000b7c2"),
        Code = "BoNoiVu",
        Name = "Bộ Nội Vụ",
        ContactEmail = "admin@moha.test",
        RolePrefix = "BNV_",
        UserCodePrefix = "BNV-SEED-",
        LeadEmailPrefix = "bnv.lead.",
        DevEmailPrefix = "bnv.dev.",
        FillEmailPrefix = "bnv",
        EmailDomain = "@moha.test",
        UserCount = 1_500,
        MaxLevel = 2,
        SeedVersion = "v1-2026-05",
    };

    /// <summary>All seed profiles, in deterministic seeding order (BTC first, then BNV).</summary>
    public static readonly IReadOnlyList<TenantSeedProfile> All = [BoTaiChinh, BoNoiVu];
}
