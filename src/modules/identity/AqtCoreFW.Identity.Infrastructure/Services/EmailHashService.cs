using System.Security.Cryptography;
using System.Text;
using AqtCoreFW.Infrastructure.Configuration;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Computes a per-tenant HMAC-SHA-256 hash of an email address for use as a
/// searchable, equality-comparable surrogate of the encrypted Email column.
///
/// Canonical form: <c>email.ToLowerInvariant().Trim()</c> before hashing — ensures that
/// "User@Example.com " and "user@example.com" produce the same hash.
///
/// Pepper source: <see cref="IPepperResolver"/> backed by Vault KV v2.
/// Null-tenant sentinel: <see cref="Guid.Empty"/> (path:
///   <c>aqt/encryption/email-pepper-00000000000000000000000000000000</c>).
/// THROWS if Vault has no pepper for the resolved tenant (provisioning gap, not a safe state).
/// </summary>
public sealed class EmailHashService(IPepperResolver pepperResolver)
{
    /// <summary>
    /// Computes HMAC-SHA-256(<paramref name="email"/> canonical form, pepper[<paramref name="tenantId"/>]).
    /// Returns a 44-char Base64 string (32 bytes → Base64).
    /// </summary>
    /// <param name="email">Raw email address — will be canonicalized before hashing.</param>
    /// <param name="tenantId">Tenant scope; <see cref="Guid.Empty"/> for global/system users.</param>
    /// <returns>Base64-encoded HMAC-SHA-256 hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="email"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">No Vault pepper for <paramref name="tenantId"/>.</exception>
    public string ComputeHash(string email, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email, nameof(email));

        var canonical = Canonicalize(email);
        var pepper = pepperResolver.GetPepper(tenantId);
        var inputBytes = Encoding.UTF8.GetBytes(canonical);

        var hashBytes = HMACSHA256.HashData(pepper, inputBytes);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Overload for nullable tenant — maps <c>null</c> → <see cref="Guid.Empty"/> sentinel.
    /// </summary>
    public string ComputeHash(string email, Guid? tenantId)
        => ComputeHash(email, tenantId ?? Guid.Empty);

    /// <summary>
    /// Canonicalize email: lowercase + trim.
    /// Two addresses that differ only in casing or surrounding whitespace MUST produce identical hashes.
    /// </summary>
    internal static string Canonicalize(string email) => email.ToLowerInvariant().Trim();
}
