using System.Security.Cryptography;
using AqtCoreFW.Infrastructure.Configuration;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic in-memory <see cref="IPepperResolver"/> for integration tests.
///
/// Production <c>VaultPepperResolver</c> reads per-tenant email-hash peppers from Vault. The test
/// harness has no Vault policy for the <c>secret/aqt/encryption/email-pepper-*</c> paths, so the real
/// resolver throws <see cref="KeyNotFoundException"/>. The write path (<c>EmailHashInterceptor</c>)
/// catches that and leaves the hash columns NULL, but the read path
/// (<c>EmailHashedUserStore.FindByEmailAsync</c>, used by the register-user duplicate-email pre-check)
/// does not — so user creation surfaces a 500. Stubbing the resolver here (mirroring the
/// <c>IEffectivePermissionService</c> stub) makes the email hash deterministic so the write and read
/// paths compute the SAME hash and lookups resolve.
///
/// The pepper is SHA-256(tenantId) — stable per tenant, distinct across tenants, and never throws.
/// </summary>
public sealed class FakePepperResolver : IPepperResolver
{
    public byte[] GetPepper(Guid tenantId) => SHA256.HashData(tenantId.ToByteArray());
}
