namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Organization-internal description of how to seed one tenant's org tree. Kept here (not in the
/// SharedKernel <c>TenantSeedProfile</c>) because it names module-specific artifacts — embedded JSON
/// resources and the BTC-only treasury ward-generation step — that SharedKernel must not know about.
/// Resolved per tenant by <see cref="OrgTreePlans"/>, keyed off the profile <c>Code</c>.
/// </summary>
/// <param name="RootCode">Expected code of the level-1 root node (sanity / logging).</param>
/// <param name="JsonResources">
/// Embedded JSON resource file names, in any order — the seeder loads them all, then seeds
/// level-ascending, resolving each node's parent by <c>ParentCode</c>. Every node carries Level +
/// ParentCode, so ordering within the list does not matter.
/// </param>
/// <param name="GenerateTreasuryWardPlaceholders">
/// BTC-only: after the JSON levels, generate L4 KBNN placeholder nodes from <c>masterdata.Wards</c>
/// (ported from the legacy treasury step). Other tenants set this false.
/// </param>
public sealed record OrgTreePlan(
    string RootCode,
    IReadOnlyList<string> JsonResources,
    bool GenerateTreasuryWardPlaceholders);
