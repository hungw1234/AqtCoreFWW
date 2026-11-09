using AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;
using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateExternalIdentityCommandValidator.
/// Verifies: required fields, email validation, length limits.
/// </summary>
public sealed class CreateExternalIdentityCommandValidatorTests
{
    private readonly CreateExternalIdentityCommandValidator _validator = new();

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            "john@example.com",
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyUserId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            Guid.Empty,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            "john@example.com",
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "UserId");
    }

    [Fact]
    public void Validate_EmptyExternalId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "",
            "John Doe",
            "john@example.com",
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ExternalId");
    }

    [Fact]
    public void Validate_ExternalIdExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            new string('x', 501),  // MaxLength = 500
            "John Doe",
            "john@example.com",
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ExternalId");
    }

    [Fact]
    public void Validate_DisplayNameExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            new string('x', 201),  // MaxLength = 200
            "john@example.com",
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "DisplayName");
    }

    [Fact]
    public void Validate_InvalidEmail_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            "not-an-email",  // Invalid email format
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_EmailExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            new string('x', 245) + "@example.com",  // Total > 254
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_NullEmail_PassesValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            null,  // Email is optional
            ActorId,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyActorId_FailsValidation()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            "john@example.com",
            Guid.Empty,
            CallerTenantId: null,
            IsPrivileged: false);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ActorId");
    }
}
