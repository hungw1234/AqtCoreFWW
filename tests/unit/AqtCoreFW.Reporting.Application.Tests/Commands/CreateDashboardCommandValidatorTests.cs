using AqtCoreFW.Reporting.Application.Commands.CreateDashboard;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Reporting.Application.Tests.Commands;

/// <summary>
/// Validator tests for CreateDashboardCommandValidator.
/// Verifies required fields and length constraints.
/// </summary>
public sealed class CreateDashboardCommandValidatorTests
{
    private readonly CreateDashboardCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var command = new CreateDashboardCommand(
            Name: "My Dashboard",
            OwnerId: Guid.NewGuid(),
            TenantId: Guid.NewGuid());

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_HasError()
    {
        var command = new CreateDashboardCommand(
            Name: "",
            OwnerId: Guid.NewGuid(),
            TenantId: Guid.NewGuid());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_HasError()
    {
        var command = new CreateDashboardCommand(
            Name: new string('A', 201),
            OwnerId: Guid.NewGuid(),
            TenantId: Guid.NewGuid());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_EmptyOwnerId_HasError()
    {
        var command = new CreateDashboardCommand(
            Name: "Dashboard",
            OwnerId: Guid.Empty,
            TenantId: Guid.NewGuid());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.OwnerId);
    }

    [Fact]
    public void Validate_EmptyTenantId_HasError()
    {
        var command = new CreateDashboardCommand(
            Name: "Dashboard",
            OwnerId: Guid.NewGuid(),
            TenantId: Guid.Empty);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.TenantId);
    }
}
