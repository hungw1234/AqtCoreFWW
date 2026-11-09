namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Single source of truth cho ABAC cache key format.
/// MUST khớp <c>TenantCacheKeyFactory</c> format (<c>t:{tenantId}:{key}</c> / <c>sys:{key}</c>)
/// để Two-Tier cache + Redis pub/sub invalidation hoạt động chéo pod đúng.
///
/// Invalidator là Singleton (không inject được Scoped <c>ITenantContext</c>),
/// handler là Scoped — cả hai đều gọi static này nên keys không drift.
/// </summary>
public static class AbacCacheKeys
{
    // Matches TenantCacheKeyFactory (Infrastructure layer). Literal ở đây để tránh layer violation
    // (Application không reference Infrastructure). Unit test AbacCacheKeysTests guard drift.
    public static string ForDepartment(Guid? tenantId, string department)
        => tenantId is null
            ? $"sys:abac:rules:department:{department}"
            : $"t:{tenantId.Value}:abac:rules:department:{department}";
}
