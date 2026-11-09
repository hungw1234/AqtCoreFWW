using System.Security.Cryptography;
using AqtCoreFW.Infrastructure.Configuration;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic in-memory <see cref="IKeyResolver"/> for integration tests.
///
/// Production <c>VaultKeyResolver</c> reads per-version AES-256 keys from Vault. The test harness has
/// no Vault policy for those paths, so <c>EncryptedStringConverter</c> (which encrypts the
/// <c>Email</c> / <c>TotpSecret</c> columns) throws <see cref="KeyNotFoundException"/> on SaveChanges
/// — surfacing as a 500 on user creation. A deterministic stub returns a stable 32-byte key per
/// version so writes encrypt and subsequent reads decrypt with the same key.
///
/// <see cref="CurrentVersion"/> is 1, matching the production <c>Encryption:CurrentVersion</c> default,
/// so written rows carry the <c>v1:</c> prefix the converter expects on read-back.
/// </summary>
public sealed class FakeKeyResolver : IKeyResolver
{
    public int CurrentVersion => 1;

    public byte[] GetKey(int version) => SHA256.HashData(BitConverter.GetBytes(version));
}
