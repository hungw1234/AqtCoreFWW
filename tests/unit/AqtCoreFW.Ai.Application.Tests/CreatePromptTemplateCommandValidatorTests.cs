using AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;
using AqtCoreFW.Ai.Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Validator tests for CreatePromptTemplateCommand.
/// Verifies prompt content constraints before any DB write.
/// </summary>
public sealed class CreatePromptTemplateCommandValidatorTests
{
    private readonly CreatePromptTemplateCommandValidator _sut = new();

    private static CreatePromptTemplateCommand Valid() => new(
        Name: "Summary Template",
        Category: PromptCategory.Summary,
        SystemPrompt: "You are a document summariser.",
        UserPromptTemplate: "Summarise: {content}");

    [Fact]
    public void ValidCommand_PassesValidation()
    {
        var result = _sut.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void NameExceeds200Chars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = new string('x', 201) });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void EmptySystemPrompt_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { SystemPrompt = "" });
        result.ShouldHaveValidationErrorFor(x => x.SystemPrompt);
    }

    [Fact]
    public void SystemPromptExceeds8000Chars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { SystemPrompt = new string('x', 8001) });
        result.ShouldHaveValidationErrorFor(x => x.SystemPrompt);
    }

    [Fact]
    public void EmptyUserPromptTemplate_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { UserPromptTemplate = "" });
        result.ShouldHaveValidationErrorFor(x => x.UserPromptTemplate);
    }

    [Fact]
    public void UserPromptTemplateExceeds4000Chars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { UserPromptTemplate = new string('x', 4001) });
        result.ShouldHaveValidationErrorFor(x => x.UserPromptTemplate);
    }

    [Theory]
    [InlineData(PromptCategory.Chat)]
    [InlineData(PromptCategory.Extraction)]
    [InlineData(PromptCategory.Summary)]
    [InlineData(PromptCategory.Classification)]
    public void AllCategories_PassValidation(PromptCategory category)
    {
        var result = _sut.TestValidate(Valid() with { Category = category });
        result.ShouldNotHaveAnyValidationErrors();
    }
}
