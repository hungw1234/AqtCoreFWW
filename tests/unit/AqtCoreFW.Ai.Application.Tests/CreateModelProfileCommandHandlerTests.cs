using AqtCoreFW.Ai.Application.Commands.CreateModelProfile;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for CreateModelProfileCommandHandler (T2 retier).
/// Verifies: IGuidGenerator injected, DTO.Id sourced from entity.PublicId (Guid), single-default invariant.
/// </summary>
public sealed class CreateModelProfileCommandHandlerTests
{
    private static readonly Guid FixedPublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000001");

    private static (CreateModelProfileCommandHandler handler, IAiModelProfileRepository repo) Setup(
        Guid? publicId = null)
    {
        var repo = Substitute.For<IAiModelProfileRepository>();
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(publicId ?? FixedPublicId);

        // GetActiveAsync returns empty list by default (no existing profiles)
        repo.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AiModelProfile>()));

        var handler = new CreateModelProfileCommandHandler(repo, gen);
        return (handler, repo);
    }

    private static CreateModelProfileCommand ValidCommand(bool isDefault = false) => new(
        Name: "GPT-4o",
        Provider: AiProvider.AzureOpenAI,
        ModelId: "gpt-4o",
        Endpoint: "https://api.openai.azure.com",
        MaxTokens: 4096,
        Temperature: 0.7m,
        CostPerToken: 0.00001m,
        IsDefault: isDefault);

    [Fact]
    public async Task Handle_ReturnsDto_WithPublicIdAsId()
    {
        var (handler, _) = Setup(FixedPublicId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(FixedPublicId);
    }

    [Fact]
    public async Task Handle_DtoId_IsGuid_NotLong()
    {
        var (handler, _) = Setup();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        // AiModelProfileDto.Id is Guid — validate it's non-empty
        result.Value.Id.Should().NotBeEmpty();
        result.Value.Id.Should().Be(FixedPublicId);
    }

    [Fact]
    public async Task Handle_PersistsProfile_ViaAddAsync()
    {
        var (handler, repo) = Setup();

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await repo.Received(1).AddAsync(Arg.Any<AiModelProfile>(), Arg.Any<CancellationToken>());
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenIsDefault_ClearsExistingDefaultFirst()
    {
        var repo = Substitute.For<IAiModelProfileRepository>();
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(FixedPublicId);

        // Existing active profile that is default — generator returns a different Guid for it
        var existingGen = Substitute.For<IGuidGenerator>();
        existingGen.NewIdMonotonic().Returns(Guid.NewGuid());
        var existing = AiModelProfile.Create(existingGen, "OldModel", AiProvider.AzureOpenAI,
            "old-model", null, 1000, 0.5m, 0m, isDefault: true);

        repo.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AiModelProfile> { existing }));

        var handler = new CreateModelProfileCommandHandler(repo, gen);

        await handler.Handle(ValidCommand(isDefault: true), CancellationToken.None);

        // The existing profile should have had ClearDefault() called (IsDefault becomes false)
        existing.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DtoMapsNameAndProvider_Correctly()
    {
        var (handler, _) = Setup();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.Value.Name.Should().Be("GPT-4o");
        result.Value.Provider.Should().Be(AiProvider.AzureOpenAI.ToString());
        result.Value.MaxTokens.Should().Be(4096);
    }
}
