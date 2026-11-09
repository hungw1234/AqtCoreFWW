using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Infrastructure.Clients;

/// <summary>
/// HTTP/JSON implementation of <see cref="IOrgUnitModuleClient"/> — the client half of the first remote
/// cross-module vertical. Registered in place of <c>InProcessOrgUnitModuleClient</c> once the OrgUnit
/// module is extracted (see <c>AddHttpOrgUnitModuleClient</c>). The typed <see cref="HttpClient"/>'s
/// <c>BaseAddress</c> and the <c>TenantPropagationHandler</c> (which stamps X-Tenant-Id / X-Correlation-Id
/// / a service token on every outbound call) are attached by the registration, not by this class.
///
/// <para><b>Behaviour parity.</b> Input-normalization, the <see cref="IOrgUnitModuleClient.MaxBatchSize"/>
/// cap, and empty-input short-circuits mirror the in-process client exactly, so swapping transports is
/// behaviour-preserving. The batch cap is enforced HERE, before the HTTP call, because the contract
/// documents the <see cref="ArgumentException"/> as guaranteed behaviour on every transport — and it
/// avoids a wasted round-trip. Unlike the in-process client this does NOT chunk the batch: a single
/// request has no SQL <c>IN</c>-list limit, so the provider owns any chunking it needs.</para>
/// </summary>
public sealed class HttpOrgUnitModuleClient(HttpClient http) : IOrgUnitModuleClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Batch lookup by IDs. Distinct-normalized; caps at <see cref="IOrgUnitModuleClient.MaxBatchSize"/>
    /// (throws before the call); empty input short-circuits with no HTTP call. Response re-keyed by Id (last-wins).</summary>
    public async Task<IReadOnlyDictionary<Guid, OrgUnitInfoDto>> GetOrgUnitsByIdsAsync(
        IEnumerable<Guid> ids, Guid? tenantId = null, CancellationToken ct = default)
    {
        var idList = ids?.Distinct().ToList();
        if (idList is null || idList.Count == 0)
            return new Dictionary<Guid, OrgUnitInfoDto>();

        if (idList.Count > IOrgUnitModuleClient.MaxBatchSize)
            throw new ArgumentException(
                $"Batch size {idList.Count} exceeds maximum {IOrgUnitModuleClient.MaxBatchSize}.",
                nameof(ids));

        var rows = await PostAsync<ByIdsRequest, OrgUnitInfoDto[]>(
            "/internal/org-units/by-ids", new ByIdsRequest(idList, tenantId), ct);

        var result = new Dictionary<Guid, OrgUnitInfoDto>(idList.Count);
        foreach (var row in rows ?? [])
            result[row.Id] = row;
        return result;
    }

    /// <summary>Single-tenant root lookup. Returns null when the provider reports no root (JSON null).</summary>
    public async Task<OrgUnitSummary?> GetRootOrgUnitForTenantAsync(
        Guid tenantId, CancellationToken ct = default) =>
        await PostAsync<RootForTenantRequest, OrgUnitSummary>(
            "/internal/org-units/root-for-tenant", new RootForTenantRequest(tenantId), ct);

    /// <summary>Batch root lookup. Caps at <see cref="IOrgUnitModuleClient.MaxBatchSize"/> on the raw count
    /// (throws before the call); empty input short-circuits. Response re-keyed by TenantId.</summary>
    public async Task<IReadOnlyDictionary<Guid, OrgUnitSummary>> GetRootOrgUnitsForTenantsAsync(
        IReadOnlyList<Guid> tenantIds, CancellationToken ct = default)
    {
        if (tenantIds is null || tenantIds.Count == 0)
            return new Dictionary<Guid, OrgUnitSummary>();

        if (tenantIds.Count > IOrgUnitModuleClient.MaxBatchSize)
            throw new ArgumentException(
                $"Batch size {tenantIds.Count} exceeds maximum {IOrgUnitModuleClient.MaxBatchSize}.",
                nameof(tenantIds));

        var distinct = tenantIds.Distinct().ToList();
        var rows = await PostAsync<RootsForTenantsRequest, OrgUnitSummary[]>(
            "/internal/org-units/roots-for-tenants", new RootsForTenantsRequest(distinct), ct);

        var result = new Dictionary<Guid, OrgUnitSummary>(distinct.Count);
        foreach (var row in rows ?? [])
            result[row.TenantId] = row;
        return result;
    }

    /// <summary>Recursive subtree expansion (root included), scoped to the tenant server-side.</summary>
    public async Task<IReadOnlyList<Guid>> ExpandSubtreeAsync(
        Guid rootId, Guid tenantId, CancellationToken ct = default)
    {
        var rows = await PostAsync<ExpandSubtreeRequest, Guid[]>(
            "/internal/org-units/expand-subtree", new ExpandSubtreeRequest(rootId, tenantId), ct);
        return rows?.ToList() ?? [];
    }

    /// <summary>Resolve active users assigned to any of the org-unit codes. Empty codes short-circuit (no HTTP call).</summary>
    public async Task<IReadOnlyList<Guid>> ResolveActiveUsersInOrgUnitsAsync(
        IReadOnlyList<string> codes, Guid tenantId, CancellationToken ct = default)
    {
        if (codes.Count == 0)
            return [];

        var rows = await PostAsync<ResolveActiveUsersRequest, Guid[]>(
            "/internal/org-units/resolve-active-users", new ResolveActiveUsersRequest(codes, tenantId), ct);
        return rows?.ToList() ?? [];
    }

    /// <summary>Return a user's canonical (UPPER-trimmed) org-unit codes. Re-materialized as an Ordinal set
    /// to match the in-process canonical-code comparer.</summary>
    public async Task<IReadOnlySet<string>> GetUserOrgUnitCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var rows = await PostAsync<UserCodesRequest, string[]>(
            "/internal/org-units/user-codes", new UserCodesRequest(userId, tenantId), ct);
        return new HashSet<string>(rows ?? [], StringComparer.Ordinal);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync(path, body, JsonOpts, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // A provider with nothing to return (e.g. "no root": 204 or an empty body, not literal JSON null)
        // yields default rather than throwing — collection callers re-materialize default as empty.
        return string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonSerializer.Deserialize<TResponse>(raw, JsonOpts);
    }

    // Wire request bodies — nested + private so the wire shape stays self-contained and never leaks into
    // *.Contracts (which would change the cross-module contract census the arch-guard counts).
    private sealed record ByIdsRequest(IReadOnlyList<Guid> Ids, Guid? TenantId);
    private sealed record RootForTenantRequest(Guid TenantId);
    private sealed record RootsForTenantsRequest(IReadOnlyList<Guid> TenantIds);
    private sealed record ExpandSubtreeRequest(Guid RootId, Guid TenantId);
    private sealed record ResolveActiveUsersRequest(IReadOnlyList<string> Codes, Guid TenantId);
    private sealed record UserCodesRequest(Guid UserId, Guid TenantId);
}
