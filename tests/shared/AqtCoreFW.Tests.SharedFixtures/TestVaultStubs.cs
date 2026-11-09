using System.Diagnostics.CodeAnalysis;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Infrastructure.Configuration;

namespace AqtCoreFW.Tests.SharedFixtures;

// Test-only stubs for Vault-backed resolvers — used by WebAppFixture and SeederSmokeFixture.
// Lets integration tests run without a live Vault server (dev-env Vault permission-denied
// blocks every fixture that boots Api/AuthServer otherwise).
//
// Both are idempotent (same input → same output) so a single test session reads/writes
// encrypted columns and email hashes deterministically.

/// <summary>
/// Fixed 32-byte key (all 0x42) for any version. Lets EncryptedStringConverter
/// roundtrip Confidential properties within a test session. NOT secret — dev/test only.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class TestKeyResolver : IKeyResolver
{
    private static readonly byte[] FixedKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
    public int CurrentVersion => 1;
    public byte[] GetKey(int version) => FixedKey;
}

/// <summary>
/// Fixed 32-byte key (all 0x5C) for HMAC chain signing.
/// Lets HmacChainService sign/verify audit entries within a test session without Vault.
/// NOT secret — dev/test only.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class TestAuditHmacKeyResolver : IAuditHmacKeyResolver
{
    private static readonly byte[] FixedKey = Enumerable.Repeat((byte)0x5C, 32).ToArray();
    public byte[] GetKey() => FixedKey;
}

/// <summary>
/// Deterministic-but-tenant-aware pepper stub — XOR-perturbed by tenantId so any
/// code path that accidentally uses a non-global pepper for a lookup that expects
/// the global pepper produces a different hash and fails FindByEmailAsync (exposing
/// P0-1-style regressions from QĐ742 §4.4 Wave-3).
///
/// Guid.Empty (global sentinel) → FixedPepper unmodified.
/// Non-empty tenantId → bytes[0] XOR 0xFF, bytes[1] XOR (tenantId.GetHashCode() &amp; 0xFF).
///
/// NOT secret — dev/test only.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class TestPepperResolver : IPepperResolver
{
    private static readonly byte[] FixedPepper = Enumerable.Repeat((byte)0xAB, 32).ToArray();

    public byte[] GetPepper(Guid tenantId)
    {
        var bytes = (byte[])FixedPepper.Clone();
        if (tenantId != Guid.Empty)
        {
            bytes[0] ^= 0xFF;
            bytes[1] ^= (byte)(tenantId.GetHashCode() & 0xFF);
        }
        return bytes;
    }
}
