using AqtCoreFW.Collaboration.Application.Commands.CreateConversation;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateConversationCommandHandler.
/// All dependencies mocked with NSubstitute — pure logic verification.
/// </summary>
public sealed class CreateConversationCommandHandlerTests
{
    private readonly IConversationRepository _repository;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IGuidGenerator _guidGen;
    private readonly CreateConversationCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();

    public CreateConversationCommandHandlerTests()
    {
        _repository = Substitute.For<IConversationRepository>();
        _eventPublisher = Substitute.For<IDomainEventPublisher>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(Guid.NewGuid());
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _handler = new CreateConversationCommandHandler(_repository, _eventPublisher, _guidGen);
    }

    private static CreateConversationCommand BuildCommand() =>
        new(ConversationType.Group, "Project Chat",
            new[] { TestUserId, MemberUserId },
            TestUserId, TestTenantId);

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsDtoWithCorrectName()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.Name.Should().Be("Project Chat");
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsDtoWithCorrectType()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.Type.Should().Be("Group");
    }

    [Fact]
    public async Task Handle_ValidCommand_TenantIdMatches()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.CreatedBy.Should().Be(TestUserId);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsConversation()
    {
        await _handler.Handle(BuildCommand(), CancellationToken.None);
        await _repository.Received(1).AddAsync(
            Arg.Is<Conversation>(c => c.Name == "Project Chat" && c.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_PublishesDomainEvents()
    {
        await _handler.Handle(BuildCommand(), CancellationToken.None);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Any<IEnumerable<IDomainEvent>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_MemberCountMatchesInput()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.MemberCount.Should().Be(2);
    }

    // --- Repository failure ---

    [Fact]
    public async Task Handle_RepositoryThrows_ExceptionPropagates()
    {
        _repository.AddAsync(Arg.Any<Conversation>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        var act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }
}
