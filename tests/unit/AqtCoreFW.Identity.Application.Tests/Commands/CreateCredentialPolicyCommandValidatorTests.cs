using AqtCoreFW.Identity.Application.Commands.CreateCredentialPolicy;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateCredentialPolicyCommandValidator.
/// Verifies: required fields, length constraints, numeric ranges, minLength <= maxLength.
/// </summary>
public sealed class CreateCredentialPolicyCommandValidatorTests
{
    private readonly CreateCredentialPolicyCommandValidator _validator = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Standard Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyName_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_NameExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            new string('x', 201),  // MaxLength = 200
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_EmptyTenantId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            Guid.Empty,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "TenantId");
    }

    [Fact]
    public void Validate_MinLengthBelowRange_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            7,  // Min is 8
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MinLength");
    }

    [Fact]
    public void Validate_MinLengthAboveRange_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            129,  // Max is 128
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MinLength");
    }

    [Fact]
    public void Validate_MaxLengthLessThanMinLength_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            100,
            50,  // MaxLength < MinLength
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxLength");
    }

    [Fact]
    public void Validate_MaxLengthAboveLimit_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            257,  // Max is 256
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxLength");
    }

    [Fact]
    public void Validate_RotationDaysNegative_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            -1,  // Must be >= 0
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "RotationDays");
    }

    [Fact]
    public void Validate_MaxFailedAttemptsBelowRange_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            0,  // Min is 1
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxFailedAttempts");
    }

    [Fact]
    public void Validate_MaxFailedAttemptsAboveRange_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            101,  // Max is 100
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxFailedAttempts");
    }

    [Fact]
    public void Validate_LockoutMinutesNegative_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            -1,  // Must be >= 0
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "LockoutMinutes");
    }

    [Fact]
    public void Validate_PasswordHistoryCountBelowRange_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            -1,  // Min is 0
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "PasswordHistoryCount");
    }

    [Fact]
    public void Validate_PasswordHistoryCountAboveRange_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            25,  // Max is 24
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "PasswordHistoryCount");
    }

    [Fact]
    public void Validate_EmptyActorId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            Guid.Empty);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ActorId");
    }

    [Fact]
    public void Validate_MinMaxLengthEqual_PassesValidation()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Policy",
            TenantId,
            10,
            10,  // Equal to MinLength
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
