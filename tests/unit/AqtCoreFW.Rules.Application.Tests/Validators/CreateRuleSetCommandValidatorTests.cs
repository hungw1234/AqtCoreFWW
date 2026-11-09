using AqtCoreFW.Rules.Application.Commands.CreateRuleSet;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Rules.Application.Tests.Validators;

public sealed class CreateRuleSetCommandValidatorTests
{
    private readonly CreateRuleSetCommandValidator _validator = new();

    [Fact]
    public void Valid_AllFields_PassesValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.NewGuid(), "Invoice Rules", "desc", "Finance", "Invoice");
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Valid_OptionalFieldsNull_PassesValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.NewGuid(), "Rules", null, null, null);
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Invalid_EmptyTenantId_FailsValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.Empty, "Rules", null, null, null);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.TenantId);
    }

    [Fact]
    public void Invalid_EmptyName_FailsValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.NewGuid(), "", null, null, null);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Invalid_NameTooLong_FailsValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.NewGuid(), new string('x', 201), null, null, null);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Invalid_DescriptionTooLong_FailsValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.NewGuid(), "Rules", new string('x', 1001), null, null);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Invalid_CategoryTooLong_FailsValidation()
    {
        var cmd = new CreateRuleSetCommand(Guid.NewGuid(), "Rules", null, new string('x', 101), null);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }
}
