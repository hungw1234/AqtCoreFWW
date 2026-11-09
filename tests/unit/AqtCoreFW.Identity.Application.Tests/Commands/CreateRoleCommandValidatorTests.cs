using AqtCoreFW.Identity.Application.Commands.CreateRole;
using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateRoleCommandValidator.
/// Verifies: required fields, code format (A-Z0-9_), name/code length, RoleType enum.
/// </summary>
public sealed class CreateRoleCommandValidatorTests
{
    private readonly CreateRoleCommandValidator _validator = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Chuyên viên xử lý",
            "CHUYEN_VIEN_XU_LY",
            "Vai trò xử lý hồ sơ",
            RoleType.Business,
            TenantId,
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
        var cmd = new CreateRoleCommand(
            "",
            "MANAGER",
            null,
            RoleType.Business,
            TenantId,
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
        var cmd = new CreateRoleCommand(
            new string('x', 201),  // MaxLength = 200
            "MANAGER",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_EmptyCode_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        // Empty code triggers both NotEmpty and Matches validators
        result.Errors.Should().Contain(e => e.PropertyName == "Code");
    }

    [Fact]
    public void Validate_CodeExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            new string('A', 101),  // MaxLength = 100
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Code");
    }

    [Fact]
    public void Validate_CodeWithLowercaseLetters_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "Manager",  // Contains lowercase
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Code");
    }

    [Fact]
    public void Validate_CodeWithSpecialCharacters_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER-ROLE",  // Contains hyphen (not A-Z, 0-9, _)
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Code");
    }

    [Fact]
    public void Validate_CodeWithSpaces_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER ROLE",  // Contains space
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Code");
    }

    [Fact]
    public void Validate_CodeWithNumbers_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager Level 1",
            "MANAGER_LEVEL_1",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CodeWithUnderscores_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER_LEVEL_2_ADMIN",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyTenantId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER",
            null,
            RoleType.Business,
            Guid.Empty,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "TenantId");
    }

    [Fact]
    public void Validate_EmptyActorId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER",
            null,
            RoleType.Business,
            TenantId,
            Guid.Empty);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ActorId");
    }

    [Fact]
    public void Validate_InvalidRoleType_FailsValidation()
    {
        // Arrange - We can't pass invalid RoleType directly (it's an enum),
        // but this documents expected enum validation
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER",
            null,
            (RoleType)999,  // Invalid enum value
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "RoleType");
    }

    [Fact]
    public void Validate_RoleTypeSystem_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Administrator",
            "ADMINISTRATOR",
            "System role",
            RoleType.System,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RoleTypeBusiness_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullDescription_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            "MANAGER",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MaxLengthName_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            new string('x', 200),  // Max allowed length
            "MANAGER",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MaxLengthCode_PassesValidation()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Manager",
            new string('A', 100),  // Max allowed length
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
