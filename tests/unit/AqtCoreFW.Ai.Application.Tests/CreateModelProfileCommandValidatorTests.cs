using AqtCoreFW.Ai.Application.Commands.CreateModelProfile;
using AqtCoreFW.Ai.Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Validator tests for CreateModelProfileCommand.
/// Verifies field constraints enforced before any DB write.
/// </summary>
public sealed class CreateModelProfileCommandValidatorTests
{
    private readonly CreateModelProfileCommandValidator _sut = new();

    private static CreateModelProfileCommand Valid() => new(
        Name: "GPT-4o",
        Provider: AiProvider.AzureOpenAI,
        ModelId: "gpt-4o",
        Endpoint: "https://api.openai.azure.com",
        MaxTokens: 4096,
        Temperature: 0.7m,
        CostPerToken: 0.00001m,
        IsDefault: false);

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
    public void EmptyModelId_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { ModelId = "" });
        result.ShouldHaveValidationErrorFor(x => x.ModelId);
    }

    [Fact]
    public void MaxTokensZero_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { MaxTokens = 0 });
        result.ShouldHaveValidationErrorFor(x => x.MaxTokens);
    }

    [Fact]
    public void MaxTokensOverLimit_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { MaxTokens = 1_000_001 });
        result.ShouldHaveValidationErrorFor(x => x.MaxTokens);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void TemperatureOutOfRange_FailsValidation(double temp)
    {
        var result = _sut.TestValidate(Valid() with { Temperature = (decimal)temp });
        result.ShouldHaveValidationErrorFor(x => x.Temperature);
    }

    [Fact]
    public void NegativeCostPerToken_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { CostPerToken = -0.01m });
        result.ShouldHaveValidationErrorFor(x => x.CostPerToken);
    }

    [Fact]
    public void NullEndpoint_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { Endpoint = null });
        result.ShouldNotHaveValidationErrorFor(x => x.Endpoint);
    }

    [Fact]
    public void EndpointExceeds500Chars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Endpoint = new string('a', 501) });
        result.ShouldHaveValidationErrorFor(x => x.Endpoint);
    }
}
