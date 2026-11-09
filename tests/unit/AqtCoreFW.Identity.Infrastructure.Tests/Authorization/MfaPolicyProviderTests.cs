using AqtCoreFW.Identity.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authorization;

/// <summary>
/// Unit tests for MfaPolicyProvider (QĐ 742 1.6.b dynamic MFA policy resolution).
/// Tests dynamic policy name parsing and delegation to base provider.
/// </summary>
public sealed class MfaPolicyProviderTests
{
    private readonly IOptions<AuthorizationOptions> _options;
    private readonly MfaPolicyProvider _sut;

    public MfaPolicyProviderTests()
    {
        _options = Options.Create(new AuthorizationOptions());
        _sut = new MfaPolicyProvider(_options);
    }

    [Fact]
    public async Task GetPolicyAsync_RequiresMfaPolicyWithValidHours_ReturnsPolicyWithRequirement()
    {
        // Arrange
        var policyName = "RequiresMfa:8";

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        policy.Should().NotBeNull();
        policy!.Requirements.Should().HaveCount(1);
        var requirement = policy.Requirements.First() as MfaRequirement;
        requirement.Should().NotBeNull();
        requirement!.MaxAgeHours.Should().Be(8);
    }

    [Fact]
    public async Task GetPolicyAsync_RequiresMfaPolicyWith24Hours_ReturnsPolicyWithCorrectMaxAge()
    {
        // Arrange
        var policyName = "RequiresMfa:24";

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        policy.Should().NotBeNull();
        policy!.Requirements.Should().HaveCount(1);
        var requirement = policy.Requirements.First() as MfaRequirement;
        requirement!.MaxAgeHours.Should().Be(24);
    }

    [Fact]
    public async Task GetPolicyAsync_RequiresMfaPolicyWithInvalidHours_ReturnNull()
    {
        // Arrange
        var policyName = "RequiresMfa:abc"; // Non-numeric suffix

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_RequiresMfaPolicyWithZeroHours_ReturnsNull()
    {
        // Arrange
        var policyName = "RequiresMfa:0"; // Invalid: must be > 0

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_RequiresMfaPolicyWithNegativeHours_ReturnsNull()
    {
        // Arrange
        var policyName = "RequiresMfa:-5";

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_NonMfaPolicy_DelegatesToBaseProvider()
    {
        // Arrange
        // First register a non-MFA policy via Options
        var options = new AuthorizationOptions();
        options.AddPolicy("Permission", policy =>
            policy.RequireClaim("permission", "edit_users"));
        var optionsInstance = Options.Create(options);
        var provider = new MfaPolicyProvider(optionsInstance);

        var policyName = "Permission";

        // Act
        var policy = await provider.GetPolicyAsync(policyName);

        // Assert
        policy.Should().NotBeNull();
        policy!.Requirements.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task GetPolicyAsync_UnknownPolicy_ReturnsNull()
    {
        // Arrange
        var policyName = "UnknownPolicy";

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_RequiresMfaPolicyCaseSensitive_DoesNotMatch()
    {
        // Arrange
        var policyName = "requiresmfa:8"; // Lowercase — should not match "RequiresMfa"

        // Act
        var policy = await _sut.GetPolicyAsync(policyName);

        // Assert
        // Prefix match is case-ordinal, so lowercase won't match
        policy.Should().BeNull();
    }
}
