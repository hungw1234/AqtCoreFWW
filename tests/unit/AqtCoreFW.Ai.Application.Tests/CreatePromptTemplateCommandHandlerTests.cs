using AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for CreatePromptTemplateCommandHandler (T2 retier).
/// Verifies: IGuidGenerator injected, DTO.Id sourced from entity.PublicId (Guid), persistence called.
/// </summary>
public sealed class CreatePromptTemplateCommandHandlerTests
{
    private static readonly Guid FixedPublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000011");

    private static (CreatePromptTemplateCommandHandler handler, IAiPromptTemplateRepository repo) Setup(
        Guid? publicId = null)
    {
        var repo = Substitute.For<IAiPromptTemplateRepository>();
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(publicId ?? FixedPublicId);

        var handler = new CreatePromptTemplateCommandHandler(repo, gen);
        return (handler, repo);
    }

    private static CreatePromptTemplateCommand ValidCommand() => new(
        Name: "Summary Template",
        Category: PromptCategory.Summary,
        SystemPrompt: "You are a document summariser.",
        UserPromptTemplate: "Summarise: {content}");

    [Fact]
    public async Task Handle_ReturnsDto_WithPublicIdAsId()
    {
        var (handler, _) = Setup(FixedPublicId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(FixedPublicId);
    }

    [Fact]
    public async Task Handle_DtoId_IsGuid_NonEmpty()
    {
        var (handler, _) = Setup();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.Value.Id.Should().NotBeEmpty();
        result.Value.Id.Should().Be(FixedPublicId);
    }

    [Fact]
    public async Task Handle_PersistsTemplate_ViaAddAndSave()
    {
        var (handler, repo) = Setup();

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await repo.Received(1).AddAsync(Arg.Any<AiPromptTemplate>(), Arg.Any<CancellationToken>());
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DtoMapsName_AndCategory_Correctly()
    {
        var (handler, _) = Setup();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.Value.Name.Should().Be("Summary Template");
        result.Value.Category.Should().Be(PromptCategory.Summary.ToString());
        result.Value.Version.Should().Be(1);
        result.Value.IsActive.Should().BeTrue();
    }
}
