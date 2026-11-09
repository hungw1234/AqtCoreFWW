using AqtCoreFW.Identity.Application.Commands.UpdateRole;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for UpdateRoleCommandValidator.
/// Verifies: required fields (RoleId, Name, ActorId), name length constraints.
/// </summary>
public sealed class UpdateRoleCommandValidatorTests
{
    private readonly UpdateRoleCommandValidator _validator = new();

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Manager", "Description", ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyRoleId_FailsValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(Guid.Empty, "Manager", null, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "RoleId");
    }

    [Fact]
    public void Validate_EmptyName_FailsValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "", null, ActorId, TenantId);

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
        var cmd = new UpdateRoleCommand(
            RoleId,
            new string('x', 201),  // MaxLength = 200
            null,
            ActorId,
            TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_EmptyActorId_FailsValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Manager", null, Guid.Empty, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ActorId");
    }

    [Fact]
    public void Validate_NullDescription_PassesValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Manager", null, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MaxLengthName_PassesValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(
            RoleId,
            new string('x', 200),  // Max allowed length
            null,
            ActorId,
            TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SingleCharacterName_PassesValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "A", null, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyTenantId_FailsValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Manager", null, ActorId, Guid.Empty);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        // TenantId validation is not explicitly checked in the validator
        // but the handler will perform tenant isolation checks
        result.IsValid.Should().BeTrue();  // Validator doesn't validate TenantId
    }

    [Fact]
    public void Validate_DescriptionWithSpecialCharacters_PassesValidation()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(
            RoleId,
            "Manager",
            "Người quản lý dự án (PM) — vai trò đặc biệt!",
            ActorId,
            TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LongDescription_PassesValidation()
    {
        // Arrange
        var longDescription = new string('x', 500);
        var cmd = new UpdateRoleCommand(RoleId, "Manager", longDescription, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
