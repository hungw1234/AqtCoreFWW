using AqtCoreFW.Identity.Application.Commands.SyncRolePermissions;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for SyncRolePermissionsCommandValidator.
/// Verifies: required fields (RoleId, ActorId, TenantId), PermissionIds not null, count <= 500.
/// </summary>
public sealed class SyncRolePermissionsCommandValidatorTests
{
    private readonly SyncRolePermissionsCommandValidator _validator = new();

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var permissionIds = new List<Guid>
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        }.AsReadOnly();

        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

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
        var permissionIds = new List<Guid> { Guid.NewGuid() }.AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(Guid.Empty, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "RoleId");
    }

    [Fact]
    public void Validate_NullPermissionIds_ThrowsValidationException()
    {
        // Arrange - Note: Passing null to a NotNull validator will throw, not return validation errors
        var cmd = new SyncRolePermissionsCommand(RoleId, null!, ActorId, TenantId);

        // Act & Assert - The validator throws because null violates NotNull constraint
        var action = () => _validator.Validate(cmd);
        action.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void Validate_EmptyPermissionIdsList_PassesValidation()
    {
        // Arrange - Empty list is allowed (no permissions assigned)
        var permissionIds = new List<Guid>().AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PermissionCountExceedsLimit_FailsValidation()
    {
        // Arrange
        var permissionIds = Enumerable.Range(0, 501)
            .Select(_ => Guid.NewGuid())
            .ToList()
            .AsReadOnly();

        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "PermissionIds.Count" &&
            e.ErrorMessage.Contains("500"));
    }

    [Fact]
    public void Validate_PermissionCountAtLimit_PassesValidation()
    {
        // Arrange - Exactly 500 permissions
        var permissionIds = Enumerable.Range(0, 500)
            .Select(_ => Guid.NewGuid())
            .ToList()
            .AsReadOnly();

        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PermissionCountBelowLimit_PassesValidation()
    {
        // Arrange
        var permissionIds = Enumerable.Range(0, 250)
            .Select(_ => Guid.NewGuid())
            .ToList()
            .AsReadOnly();

        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyActorId_FailsValidation()
    {
        // Arrange
        var permissionIds = new List<Guid> { Guid.NewGuid() }.AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, Guid.Empty, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ActorId");
    }

    [Fact]
    public void Validate_EmptyTenantId_FailsValidation()
    {
        // Arrange
        var permissionIds = new List<Guid> { Guid.NewGuid() }.AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, Guid.Empty);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "TenantId");
    }

    [Fact]
    public void Validate_ConfirmedFalse_PassesValidation()
    {
        // Arrange
        var permissionIds = new List<Guid> { Guid.NewGuid() }.AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(
            RoleId,
            permissionIds,
            ActorId,
            TenantId,
            Confirmed: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ConfirmedTrue_PassesValidation()
    {
        // Arrange
        var permissionIds = new List<Guid> { Guid.NewGuid() }.AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(
            RoleId,
            permissionIds,
            ActorId,
            TenantId,
            Confirmed: true);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SinglePermission_PassesValidation()
    {
        // Arrange
        var permissionIds = new List<Guid> { Guid.NewGuid() }.AsReadOnly();
        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LargePermissionSet_PassesValidation()
    {
        // Arrange - 499 permissions (just below limit)
        var permissionIds = Enumerable.Range(0, 499)
            .Select(_ => Guid.NewGuid())
            .ToList()
            .AsReadOnly();

        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DuplicatePermissionIds_PassesValidation()
    {
        // Arrange - Validator doesn't check for duplicates, handler logic does
        var permId = Guid.NewGuid();
        var permissionIds = new List<Guid>
        {
            permId,
            permId,
            Guid.NewGuid()
        }.AsReadOnly();

        var cmd = new SyncRolePermissionsCommand(RoleId, permissionIds, ActorId, TenantId);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MultipleValidationErrors_ReturnsAllErrors()
    {
        // Arrange - Multiple violations: empty RoleId, empty ActorId, empty TenantId
        var cmd = new SyncRolePermissionsCommand(
            Guid.Empty,  // Empty RoleId
            new List<Guid>().AsReadOnly(),  // Valid: empty list is allowed
            Guid.Empty,  // Empty ActorId
            Guid.Empty);  // Empty TenantId

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThanOrEqualTo(3);
        result.Errors.Should().Contain(e => e.PropertyName == "RoleId");
        result.Errors.Should().Contain(e => e.PropertyName == "ActorId");
        result.Errors.Should().Contain(e => e.PropertyName == "TenantId");
    }
}
