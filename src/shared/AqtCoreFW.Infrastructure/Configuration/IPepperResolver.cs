namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Runtime per-tenant pepper resolver backed by HashiCorp Vault.
/// THROWS on miss — a missing pepper indicates a provisioning gap, not a safe fallback state.
/// </summary>
public interface IPepperResolver
{
    /// <summary>
    /// Fetches the raw pepper bytes for the given tenant.
    /// Cached for 5 minutes (sliding).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Vault has no pepper for <paramref name="tenantId"/>.</exception>
    byte[] GetPepper(Guid tenantId);
}
