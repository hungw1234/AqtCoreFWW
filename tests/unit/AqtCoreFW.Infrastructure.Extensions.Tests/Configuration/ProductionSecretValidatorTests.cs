using AqtCoreFW.Infrastructure.Configuration;
using FluentAssertions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="ProductionSecretValidator.AssertSecretValid"/>.
/// Tests the validation logic directly (isolated from IHost wiring).
/// </summary>
public sealed class ProductionSecretValidatorTests
{
    // ── Happy path ─────────────────────────────────────────────────────────

    [Fact]
    public void AssertSecretValid_ValidProductionValue_DoesNotThrow()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "ConnectionStrings:Default",
            "Server=prod-db;Database=AqtCoreFW;...");

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertSecretValid_Base64Key_DoesNotThrow()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "Encryption:FieldKey",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=");

        act.Should().NotThrow();
    }

    // ── Null/empty failures ────────────────────────────────────────────────

    [Fact]
    public void AssertSecretValid_NullValue_Throws()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "ConnectionStrings:Default",
            null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production secret missing or contains DEV marker: ConnectionStrings:Default*");
    }

    [Fact]
    public void AssertSecretValid_EmptyValue_Throws()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "Encryption:FieldKey",
            string.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Encryption:FieldKey*");
    }

    [Fact]
    public void AssertSecretValid_WhitespaceValue_Throws()
    {
        // M4 (S4 review): IsNullOrWhiteSpace catches config typos like "   ".
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "ConnectionStrings:Default",
            "   ");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Default*");
    }

    // ── DEV_ prefix failures ───────────────────────────────────────────────

    [Fact]
    public void AssertSecretValid_DevPrefixValue_Throws()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "Encryption:FieldKey",
            "DEV_SOME_PLACEHOLDER_VALUE");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production secret missing or contains DEV marker: Encryption:FieldKey*");
    }

    [Fact]
    public void AssertSecretValid_DevUnderscorePrefix_Throws()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "Audit:HmacKey",
            "DEV_HMAC_KEY_HERE");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Audit:HmacKey*");
    }

    // ── Known dev-marker failures ──────────────────────────────────────────

    [Fact]
    public void AssertSecretValid_KnownDevMarker_FieldKey_Throws()
    {
        // This exact value is the appsettings.Development.json FieldKey default
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "Encryption:FieldKey",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Encryption:FieldKey*");
    }

    [Fact]
    public void AssertSecretValid_KnownDevMarker_DevPlaceholder_Throws()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "ConnectionStrings:Default",
            "DEV_PLACEHOLDER");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Default*");
    }

    // ── Error message content ──────────────────────────────────────────────

    [Fact]
    public void AssertSecretValid_ErrorMessage_ContainsKeyName()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(
            "Audit:HmacKey",
            null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Production secret missing or contains DEV marker: Audit:HmacKey");
    }

    // ── All required keys round-trip ───────────────────────────────────────

    [Theory]
    [InlineData("ConnectionStrings:Default", "Server=prod;Database=X;...")]
    [InlineData("Encryption:FieldKey", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=")]
    [InlineData("Audit:HmacKey", "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC=")]
    public void AssertSecretValid_AllRequiredKeys_ValidValues_DoNotThrow(string key, string value)
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(key, value);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("ConnectionStrings:Default", null)]
    [InlineData("ConnectionStrings:Default", "")]
    [InlineData("ConnectionStrings:Default", "DEV_CONN_STRING")]
    [InlineData("Encryption:FieldKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("Audit:HmacKey", "DEV_HMAC")]
    public void AssertSecretValid_AllRequiredKeys_DevValues_Throw(string key, string? value)
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(key, value);
        act.Should().Throw<InvalidOperationException>();
    }
}
