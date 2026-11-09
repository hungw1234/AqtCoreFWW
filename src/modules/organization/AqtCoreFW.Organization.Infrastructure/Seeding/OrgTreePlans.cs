using AqtCoreFW.SharedKernel.Application.Seeding;

namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Maps a tenant (by <see cref="TenantSeedProfile.Code"/>) to its <see cref="OrgTreePlan"/>.
/// This is the Organization-module half of the tenant-agnostic seeder: the SharedKernel profile
/// supplies identity/scale, this supplies the tree data sources.
/// </summary>
public static class OrgTreePlans
{
    /// <summary>
    /// Bộ Tài chính — the full BTC tree: 1 root + 35 L2 + 109 L3 (5 groups) + L4 tax/customs/
    /// statistics from JSON, plus the treasury ward-placeholder generation step.
    /// </summary>
    public static readonly OrgTreePlan BoTaiChinh = new(
        RootCode: "BTC",
        JsonResources:
        [
            "btc-level-1-2.json",
            "btc-level-3-tax.json",
            "btc-level-3-customs.json",
            "btc-level-3-treasury.json",
            "btc-level-3-reserve.json",
            "btc-level-3-statistics.json",
            "btc-level-4-tax-teams.json",
            "btc-level-4-customs-checkpoints.json",
            "btc-level-4-statistics-teams.json",
        ],
        GenerateTreasuryWardPlaceholders: true);

    /// <summary>
    /// Bộ Nội Vụ — post-merger 2-level structure (Nghị định 25/2025): 1 root + 22 second-level units
    /// (10 Vụ + 5 Cục + 3 Ban/Thanh tra/Văn phòng + 4 sự nghiệp). No treasury step (no KBNN under BNV)
    /// and no level-3+ data (department-internal structure is not public).
    /// </summary>
    public static readonly OrgTreePlan BoNoiVu = new(
        RootCode: "BNV",
        JsonResources: ["bnv-level-1-2.json"],
        GenerateTreasuryWardPlaceholders: false);

    private static readonly IReadOnlyDictionary<string, OrgTreePlan> ByCode =
        new Dictionary<string, OrgTreePlan>(StringComparer.OrdinalIgnoreCase)
        {
            [TenantSeedProfiles.BoTaiChinh.Code] = BoTaiChinh,
            [TenantSeedProfiles.BoNoiVu.Code] = BoNoiVu,
        };

    /// <summary>Returns the tree plan for a tenant code, or null if none is defined yet.</summary>
    public static OrgTreePlan? TryGet(string code) =>
        ByCode.TryGetValue(code, out var plan) ? plan : null;
}
