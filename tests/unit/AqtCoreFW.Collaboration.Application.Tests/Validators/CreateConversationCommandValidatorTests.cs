using AqtCoreFW.Collaboration.Application.Commands.CreateConversation;
using AqtCoreFW.Collaboration.Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Collaboration.Application.Tests.Validators;

/// <summary>
/// Unit tests for CreateConversationCommandValidator.
/// Verifies all FluentValidation rules in isolation.
/// </summary>
public sealed class CreateConversationCommandValidatorTests
{
    private readonly CreateConversationCommandValidator _validator = new();

    private static CreateConversationCommand ValidCommand() =>
        new(ConversationType.Group, "Chat Room",
            new[] { Guid.NewGuid() },
            Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Valid_AllFields_PassesValidation()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Valid_NullName_PassesValidation()
    {
        var cmd = ValidCommand() with { Name = null };
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Invalid_EmptyCreatedBy_FailsValidation()
    {
        var cmd = ValidCommand() with { CreatedBy = Guid.Empty };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.CreatedBy);
    }

    [Fact]
    public void Invalid_EmptyTenantId_FailsValidation()
    {
        var cmd = ValidCommand() with { TenantId = Guid.Empty };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.TenantId);
    }

    [Fact]
    public void Invalid_EmptyMemberList_FailsValidation()
    {
        var cmd = ValidCommand() with { MemberUserIds = Array.Empty<Guid>() };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.MemberUserIds);
    }

    [Fact]
    public void Invalid_NameTooLong_FailsValidation()
    {
        var cmd = ValidCommand() with { Name = new string('x', 201) };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Invalid_InvalidType_FailsValidation()
    {
        var cmd = new CreateConversationCommand(
            (ConversationType)99, "Chat", new[] { Guid.NewGuid() },
            Guid.NewGuid(), Guid.NewGuid());
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Type);
    }
}
