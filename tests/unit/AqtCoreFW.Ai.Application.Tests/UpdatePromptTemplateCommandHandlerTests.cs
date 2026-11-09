using AqtCoreFW.Ai.Application.Commands.UpdatePromptTemplate;
using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for UpdatePromptTemplateCommandHandler (T2 retier).
/// Verifies: handler resolves entity via GetByPublicIdAsync, Version increments on prompt update,
/// returns DTO with PublicId as Id, returns fail for unknown publicId.
/// </summary>
public sealed class UpdatePromptTemplateCommandHandlerTests
{
    private static readonly Guid TemplatePublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000012");

    private static AiPromptTemplate MakeTemplate()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(TemplatePublicId);
        return AiPromptTemplate.Create(
            gen,
            "Summary Template",
            PromptCategory.Summary,
            "You are a document summariser.",
            "Summarise: {content}");
    }

    private static (UpdatePromptTemplateCommandHandler handler, IAiPromptTemplateRepository repo) Setup(
        AiPromptTemplate? template = null)
    {
        var repo = Substitute.For<IAiPromptTemplateRepository>();
        var entity = template ?? MakeTemplate();

        repo.GetByPublicIdAsync(TemplatePublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AiPromptTemplate?>(entity));

        var handler = new UpdatePromptTemplateCommandHandler(repo);
        return (handler, repo);
    }

    [Fact]
    public async Task Handle_ResolvesEntity_ViaGetByPublicIdAsync()
    {
        var (handler, repo) = Setup();
        var command = new UpdatePromptTemplateCommand(TemplatePublicId, null, null, null);

        await handler.Handle(command, CancellationToken.None);

        await repo.Received(1).GetByPublicIdAsync(TemplatePublicId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsDto_WithPublicIdAsId()
    {
        var (handler, _) = Setup();
        var command = new UpdatePromptTemplateCommand(TemplatePublicId, "New system.", "New user: {x}", null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(TemplatePublicId);
    }

    [Fact]
    public async Task Handle_UpdatePrompts_IncrementsVersion()
    {
        var template = MakeTemplate();
        var (handler, _) = Setup(template);
        var originalVersion = template.Version;

        var command = new UpdatePromptTemplateCommand(TemplatePublicId, "New system.", "New user: {x}", null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(originalVersion + 1);
    }

    [Fact]
    public async Task Handle_Deactivate_SetsIsActiveFalse()
    {
        var template = MakeTemplate();
        var (handler, _) = Setup(template);

        var command = new UpdatePromptTemplateCommand(TemplatePublicId, null, null, IsActive: false);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnknownPublicId_ReturnsFail()
    {
        var repo = Substitute.For<IAiPromptTemplateRepository>();
        var unknownId = Guid.NewGuid();
        repo.GetByPublicIdAsync(unknownId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AiPromptTemplate?>(null));

        var handler = new UpdatePromptTemplateCommandHandler(repo);
        var command = new UpdatePromptTemplateCommand(unknownId, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(unknownId.ToString()));
    }

    [Fact]
    public async Task Handle_NoPromptChange_VersionUnchanged()
    {
        var template = MakeTemplate();
        var (handler, _) = Setup(template);
        var originalVersion = template.Version;

        // No SystemPrompt / UserPromptTemplate supplied — no prompt update, no version increment
        var command = new UpdatePromptTemplateCommand(TemplatePublicId, null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(originalVersion);
    }
}
