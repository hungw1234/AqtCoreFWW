using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>Unit tests for AiPromptTemplate domain entity (T2 retier — PublicId Guid AK).</summary>
public sealed class AiPromptTemplateEntityTests
{
    private static readonly Guid FixedPublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000010");

    private static IGuidGenerator MakeGen(Guid? id = null)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(id ?? FixedPublicId);
        return gen;
    }

    private static AiPromptTemplate MakeTemplate() =>
        AiPromptTemplate.Create(
            gen: MakeGen(),
            name: "Summary Template",
            category: PromptCategory.Summary,
            systemPrompt: "You are a document summariser.",
            userPromptTemplate: "Summarise the following: {content}");

    [Fact]
    public void Create_SetsAllProperties_VersionOneAndActive()
    {
        var template = MakeTemplate();

        template.Name.Should().Be("Summary Template");
        template.Category.Should().Be(PromptCategory.Summary);
        template.SystemPrompt.Should().Be("You are a document summariser.");
        template.UserPromptTemplate.Should().Contain("{content}");
        template.Version.Should().Be(1);
        template.IsActive.Should().BeTrue();
        template.PublicId.Should().Be(FixedPublicId);
    }

    [Fact]
    public void IncrementVersion_IncreasesByOne()
    {
        var template = MakeTemplate();
        template.IncrementVersion();
        template.Version.Should().Be(2);
    }

    [Fact]
    public void IncrementVersion_MultipleTimes_AccumulatesCorrectly()
    {
        var template = MakeTemplate();
        template.IncrementVersion();
        template.IncrementVersion();
        template.IncrementVersion();
        template.Version.Should().Be(4);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var template = MakeTemplate();
        template.Deactivate();
        template.IsActive.Should().BeFalse();
    }

    [Fact]
    public void UpdatePrompts_ChangesContentAndIncrementsVersion()
    {
        var template = MakeTemplate();
        var originalVersion = template.Version;

        template.UpdatePrompts("New system prompt.", "New user prompt: {text}");

        template.SystemPrompt.Should().Be("New system prompt.");
        template.UserPromptTemplate.Should().Be("New user prompt: {text}");
        template.Version.Should().Be(originalVersion + 1);
    }

    [Fact]
    public void UpdatePrompts_SetsUpdatedAt()
    {
        var template = MakeTemplate();
        template.UpdatePrompts("Updated system.", "Updated user.");
        template.UpdatedAt.Should().NotBeNull();
    }
}
