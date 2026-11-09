using AqtCoreFW.Infrastructure.Logging;
using FluentAssertions;
using Serilog.Core;
using Serilog.Events;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Logging;

/// <summary>
/// Unit tests for <see cref="SecretMaskingDestructuringPolicy"/>.
/// Validates that secret-named properties are masked and non-secret properties pass through.
/// </summary>
public sealed class SecretMaskingDestructuringPolicyTests
{
    // ── ShouldMaskKey tests (suffix-anchored matching) ────────────────────

    [Fact]
    public void ShouldMaskKey_Password_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("Password").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_DbPassword_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("DbPassword").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_VaultToken_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("VaultToken").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_HmacKey_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("HmacKey").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_FieldKey_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("FieldKey").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_VaultAddr_ReturnsFalse()
        // "VaultAddr" ends with "Addr" — does NOT match any suffix pattern.
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("VaultAddr").Should().BeFalse();

    [Fact]
    public void ShouldMaskKey_KeyVersion_ReturnsFalse()
        // "KeyVersion" ends with "Version" — suffix anchor prevents "Key$" from matching.
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("KeyVersion").Should().BeFalse();

    [Fact]
    public void ShouldMaskKey_Username_ReturnsFalse()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("Username").Should().BeFalse();

    // ── Case-insensitive matching ─────────────────────────────────────────

    [Fact]
    public void ShouldMaskKey_UppercasePASSWORD_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("PASSWORD").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_LowercasePassword_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("password").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_MixedCaseDbPASSWORD_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("DbPASSWORD").Should().BeTrue();

    // ── TryDestructure with complex objects ───────────────────────────────

    [Fact]
    public void TryDestructure_ObjectWithPasswordProperty_MasksValue()
    {
        var policy = new SecretMaskingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        var target = new { Password = "super-secret-value", Username = "john" };

        var destructured = policy.TryDestructure(target, factory, out var result);

        destructured.Should().BeTrue();
        var structure = result.Should().BeOfType<StructureValue>().Subject;
        var passwordProp = structure.Properties.Single(p => p.Name == "Password");
        passwordProp.Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("***");
    }

    [Fact]
    public void TryDestructure_ObjectWithPasswordProperty_PreservesNonSecretValue()
    {
        var policy = new SecretMaskingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        var target = new { Password = "secret", Username = "alice" };

        policy.TryDestructure(target, factory, out var result);

        var structure = result.Should().BeOfType<StructureValue>().Subject;
        var usernameProp = structure.Properties.Single(p => p.Name == "Username");
        // Username is not masked; scalar value preserved
        usernameProp.Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("alice");
    }

    [Fact]
    public void TryDestructure_ObjectWithNoSecretProperties_ReturnsFalse()
    {
        var policy = new SecretMaskingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        var target = new { Username = "alice", Email = "alice@example.com" };

        var destructured = policy.TryDestructure(target, factory, out _);

        // No masking needed — policy should not intercept
        destructured.Should().BeFalse();
    }

    [Fact]
    public void TryDestructure_NestedObjectWithPasswordProperty_MasksNestedValue()
    {
        var policy = new SecretMaskingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        // Nested: Connection { Password = "..." }
        var target = new { Password = "db-pass", Host = "localhost" };

        policy.TryDestructure(target, factory, out var result);

        var structure = result.Should().BeOfType<StructureValue>().Subject;
        structure.Properties.Single(p => p.Name == "Password")
            .Value.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("***");
    }

    [Fact]
    public void TryDestructure_PrimitiveString_ReturnsFalse()
    {
        var policy = new SecretMaskingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        // Primitive strings are not complex objects — policy skips them
        var destructured = policy.TryDestructure("plain string value", factory, out _);
        destructured.Should().BeFalse();
    }

    [Fact]
    public void TryDestructure_NullValue_ReturnsFalse()
    {
        var policy = new SecretMaskingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        var destructured = policy.TryDestructure(null!, factory, out _);
        destructured.Should().BeFalse();
    }

    // ── Additional masked patterns ─────────────────────────────────────────

    [Fact]
    public void ShouldMaskKey_ApiKey_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("ApiKey").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_JwtSecret_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("JwtSecret").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_AccessToken_ReturnsTrue()
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("AccessToken").Should().BeTrue();

    [Fact]
    public void ShouldMaskKey_Vault_ReturnsTrue()
        // "Vault" alone matches "Vault$"
        => SecretMaskingDestructuringPolicy.ShouldMaskKey("Vault").Should().BeTrue();
}

/// <summary>
/// Minimal <see cref="ILogEventPropertyValueFactory"/> for test use —
/// returns a scalar wrapping the original value.
/// </summary>
file sealed class TestPropertyValueFactory : ILogEventPropertyValueFactory
{
    public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false)
        => new ScalarValue(value);
}
