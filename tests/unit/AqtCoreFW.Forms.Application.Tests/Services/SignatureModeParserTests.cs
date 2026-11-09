using AqtCoreFW.Forms.Application.Services;
using FluentAssertions;

namespace AqtCoreFW.Forms.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SignatureModeParser"/>. The parser reads the signing mode from a
/// DigitalSignature field's server-side ValidationRulesJson; it must NEVER throw on a malformed or
/// mistyped value — any shape mismatch falls back to the safe CanonicalJson (Mode-1) default,
/// because an uncaught exception here would 500 every submit for the affected template.
/// </summary>
public sealed class SignatureModeParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrEmpty_DefaultsToCanonicalJson(string? json)
        => SignatureModeParser.Parse(json).Should().Be(SignatureMode.CanonicalJson);

    [Fact]
    public void Parse_ServerDocHash_ReturnsServerDocHash()
        => SignatureModeParser.Parse("""{"signatureMode":"ServerDocHash"}""")
            .Should().Be(SignatureMode.ServerDocHash);

    [Fact]
    public void Parse_ServerDocHash_IsCaseInsensitive()
        => SignatureModeParser.Parse("""{"signatureMode":"serverdochash"}""")
            .Should().Be(SignatureMode.ServerDocHash);

    [Fact]
    public void Parse_CanonicalJsonValue_ReturnsCanonicalJson()
        => SignatureModeParser.Parse("""{"signatureMode":"CanonicalJson"}""")
            .Should().Be(SignatureMode.CanonicalJson);

    [Fact]
    public void Parse_MissingKey_DefaultsToCanonicalJson()
        => SignatureModeParser.Parse("""{"minLength":3}""")
            .Should().Be(SignatureMode.CanonicalJson);

    [Fact]
    public void Parse_MalformedJson_DefaultsToCanonicalJson()
        => SignatureModeParser.Parse("{ not json")
            .Should().Be(SignatureMode.CanonicalJson);

    // M1 guard: a mistyped non-string signatureMode must NOT throw (GetString would throw
    // InvalidOperationException, which is not a JsonException) — falls back to the default.
    [Theory]
    [InlineData("""{"signatureMode":2}""")]
    [InlineData("""{"signatureMode":true}""")]
    [InlineData("""{"signatureMode":null}""")]
    [InlineData("""{"signatureMode":{"x":1}}""")]
    [InlineData("""{"signatureMode":["ServerDocHash"]}""")]
    public void Parse_NonStringSignatureMode_DefaultsToCanonicalJsonWithoutThrowing(string json)
        => SignatureModeParser.Parse(json).Should().Be(SignatureMode.CanonicalJson);

    // M1 guard: a non-object root (TryGetProperty throws InvalidOperationException) must not throw.
    [Theory]
    [InlineData("5")]
    [InlineData("\"ServerDocHash\"")]
    [InlineData("[1,2,3]")]
    public void Parse_NonObjectRoot_DefaultsToCanonicalJsonWithoutThrowing(string json)
        => SignatureModeParser.Parse(json).Should().Be(SignatureMode.CanonicalJson);
}
