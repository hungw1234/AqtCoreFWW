using AqtCoreFW.Identity.Application.Commands.CreateJitProviderConfig;
using AqtCoreFW.Identity.Domain.Entities;
using FluentValidation.TestHelper;
using FluentAssertions;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateJitProviderConfigCommandValidator.
/// Verifies: required fields, length constraints, valid values.
/// </summary>
public sealed class CreateJitProviderConfigCommandValidatorTests
{
    private readonly CreateJitProviderConfigCommandValidator _validator;

    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public CreateJitProviderConfigCommandValidatorTests()
    {
        _validator = new CreateJitProviderConfigCommandValidator();
    }

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "ValidSSO", "Valid SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 100, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyScheme_HasError()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "", "Name", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Scheme);
    }

    [Fact]
    public void Validate_SchemeTooLong_HasError()
    {
        // Arrange
        var longScheme = new string('a', 101);
        var cmd = new CreateJitProviderConfigCommand(
            longScheme, "Name", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Scheme);
    }

    [Fact]
    public void Validate_EmptyDisplayName_HasError()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "Scheme", "", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Validate_DisplayNameTooLong_HasError()
    {
        // Arrange
        var longName = new string('a', 201);
        var cmd = new CreateJitProviderConfigCommand(
            "Scheme", longName, ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Validate_EmptyDefaultRoleName_HasError()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "Scheme", "Name", ExternalIdentityProvider.SSO,
            true, "", false, null, TenantId, null, 0, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.DefaultRoleName);
    }

    [Fact]
    public void Validate_NegativeMaxProvisionsPerHour_HasError()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "Scheme", "Name", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, -1, ActorId);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MaxProvisionsPerHour);
    }

    [Fact]
    public void Validate_EmptyActorId_HasError()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "Scheme", "Name", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, Guid.Empty);

        // Act
        var result = _validator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ActorId);
    }
}
