using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>Unit tests for AiModelProfile domain entity (T2 retier — factory takes IGuidGenerator).</summary>
public sealed class AiModelProfileEntityTests
{
    private static readonly Guid FixedPublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000099");

    private static IGuidGenerator MakeGen(Guid? id = null)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(id ?? FixedPublicId);
        return gen;
    }

    private static AiModelProfile MakeProfile(bool isDefault = false) =>
        AiModelProfile.Create(
            gen: MakeGen(),
            name: "GPT-4o",
            provider: AiProvider.AzureOpenAI,
            modelId: "gpt-4o",
            endpoint: "https://my.openai.azure.com",
            maxTokens: 4096,
            temperature: 0.7m,
            costPerToken: 0.00001m,
            isDefault: isDefault);

    [Fact]
    public void Create_SetsAllProperties_AndIsActive()
    {
        var profile = MakeProfile();

        profile.Name.Should().Be("GPT-4o");
        profile.Provider.Should().Be(AiProvider.AzureOpenAI);
        profile.ModelId.Should().Be("gpt-4o");
        profile.MaxTokens.Should().Be(4096);
        profile.Temperature.Should().Be(0.7m);
        profile.IsActive.Should().BeTrue();
        profile.IsDefault.Should().BeFalse();
        // T2 entity: PublicId set by factory via IGuidGenerator
        profile.PublicId.Should().Be(FixedPublicId);
    }

    [Fact]
    public void Create_WithIsDefault_SetsDefaultTrue()
    {
        var profile = MakeProfile(isDefault: true);
        profile.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Create_NullGenerator_ThrowsArgumentNullException()
    {
        var act = () => AiModelProfile.Create(
            gen: null!,
            name: "x", provider: AiProvider.AzureOpenAI, modelId: "m",
            endpoint: null, maxTokens: 100, temperature: 0.5m, costPerToken: 0m);

        act.Should().Throw<ArgumentNullException>().WithParameterName("gen");
    }

    [Fact]
    public void SetAsDefault_MarksProfileAsDefault()
    {
        var profile = MakeProfile();
        profile.SetAsDefault();
        profile.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void ClearDefault_RemovesDefaultFlag()
    {
        var profile = MakeProfile(isDefault: true);
        profile.ClearDefault();
        profile.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalseAndClearsDefault()
    {
        var profile = MakeProfile(isDefault: true);
        profile.Deactivate();

        profile.IsActive.Should().BeFalse();
        profile.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void UpdateConfig_ChangesMaxTokensAndTemperature()
    {
        var profile = MakeProfile();
        profile.UpdateConfig(8192, 0.3m);

        profile.MaxTokens.Should().Be(8192);
        profile.Temperature.Should().Be(0.3m);
    }

    [Fact]
    public void UpdateConfig_SetsUpdatedAt()
    {
        var profile = MakeProfile();
        profile.UpdateConfig(1024, 1.0m);
        profile.UpdatedAt.Should().NotBeNull();
    }
}
