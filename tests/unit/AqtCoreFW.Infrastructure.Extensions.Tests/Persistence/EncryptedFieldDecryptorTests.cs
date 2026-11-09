using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Persistence;

/// <summary>
/// Tests for EncryptedFieldDecryptor — the read-model decryptor that lets Dapper paths decrypt
/// PII columns (e.g. AspNetUsers.Email) through the same crypto as the EF value converter.
///
/// Covers: v{N}: round-trip, null/empty passthrough, and graceful passthrough of non-ciphertext
/// (so a read-model list is never faulted by one undecodable row).
/// </summary>
public sealed class EncryptedFieldDecryptorTests
{
    private static readonly byte[] KeyV1 = new byte[32]; // all-zero test key

    private static EncryptedFieldDecryptor BuildDecryptor()
    {
        var keyResolver = Substitute.For<IKeyResolver>();
        keyResolver.CurrentVersion.Returns(1);
        keyResolver.GetKey(Arg.Any<int>()).Returns(KeyV1);
        return new EncryptedFieldDecryptor(keyResolver, NullLogger<EncryptedFieldDecryptor>.Instance);
    }

    /// <summary>Encrypts a value with the same key the decryptor resolves, producing a v1: cipher.</summary>
    private static string EncryptV1(string plaintext)
    {
        var converter = new EncryptedStringConverter(_ => KeyV1, currentVersion: 1);
        return converter.ConvertToProviderExpression.Compile()(plaintext);
    }

    [Fact]
    public void Decrypt_V1Cipher_ReturnsPlaintext()
    {
        var cipher = EncryptV1("admin@gov.vn");
        cipher.Should().MatchRegex(@"^v\d+:", because: "the converter writes a versioned prefix");

        BuildDecryptor().Decrypt(cipher).Should().Be("admin@gov.vn");
    }

    [Fact]
    public void Decrypt_Null_ReturnsNull()
        => BuildDecryptor().Decrypt(null).Should().BeNull();

    [Fact]
    public void Decrypt_Empty_ReturnsEmpty()
        => BuildDecryptor().Decrypt(string.Empty).Should().BeEmpty();

    [Fact]
    public void Decrypt_NonCiphertext_ReturnsInputUnchanged()
    {
        // A plaintext value (no v{N}: prefix, not valid base64) must pass through, not throw —
        // a read-model enrichment must never fault a whole list on one already-plaintext row.
        const string plain = "legacy.plaintext@gov.vn";
        BuildDecryptor().Decrypt(plain).Should().Be(plain);
    }
}
