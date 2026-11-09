using AqtCoreFW.Ai.Application.Commands.UpdateModelProfile;
using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for UpdateModelProfileCommandHandler (T2 retier).
/// Verifies: handler resolves entity via GetByPublicIdAsync, returns DTO with PublicId as Id.
/// </summary>
public sealed class UpdateModelProfileCommandHandlerTests
{
    private static readonly Guid ProfilePublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000002");

    private static AiModelProfile MakeProfile(bool isDefault = false)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(ProfilePublicId);
        return AiModelProfile.Create(gen, "GPT-4o", AiProvider.AzureOpenAI,
            "gpt-4o", null, 4096, 0.7m, 0.00001m, isDefault);
    }

    private static (UpdateModelProfileCommandHandler handler, IAiModelProfileRepository repo) Setup(
        AiModelProfile? profile = null)
    {
        var repo = Substitute.For<IAiModelProfileRepository>();
        var entity = profile ?? MakeProfile();

        repo.GetByPublicIdAsync(ProfilePublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AiModelProfile?>(entity));
        repo.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AiModelProfile> { entity }));

        var handler = new UpdateModelProfileCommandHandler(repo);
        return (handler, repo);
    }

    [Fact]
    public async Task Handle_ResolvesEntity_ViaGetByPublicIdAsync()
    {
        var (handler, repo) = Setup();

        var command = new UpdateModelProfileCommand(ProfilePublicId, MaxTokens: 8192, null, null, null);
        await handler.Handle(command, CancellationToken.None);

        await repo.Received(1).GetByPublicIdAsync(ProfilePublicId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsDto_WithPublicIdAsId()
    {
        var (handler, _) = Setup();

        var command = new UpdateModelProfileCommand(ProfilePublicId, MaxTokens: 8192, null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(ProfilePublicId);
    }

    [Fact]
    public async Task Handle_UnknownPublicId_ReturnsFail()
    {
        var repo = Substitute.For<IAiModelProfileRepository>();
        var unknownId = Guid.NewGuid();
        repo.GetByPublicIdAsync(unknownId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AiModelProfile?>(null));

        var handler = new UpdateModelProfileCommandHandler(repo);
        var command = new UpdateModelProfileCommand(unknownId, null, null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(unknownId.ToString()));
    }

    [Fact]
    public async Task Handle_UpdateMaxTokens_AppliesChange()
    {
        var profile = MakeProfile();
        var (handler, _) = Setup(profile);

        var command = new UpdateModelProfileCommand(ProfilePublicId, MaxTokens: 16384, null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxTokens.Should().Be(16384);
    }

    [Fact]
    public async Task Handle_Deactivate_SetsIsActiveFalse()
    {
        var profile = MakeProfile();
        var (handler, _) = Setup(profile);

        var command = new UpdateModelProfileCommand(ProfilePublicId, null, null, null, IsActive: false);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
    }
}
