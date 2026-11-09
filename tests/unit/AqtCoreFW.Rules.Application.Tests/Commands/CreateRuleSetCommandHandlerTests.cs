using AqtCoreFW.Rules.Application.Commands.CreateRuleSet;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateRuleSetCommandHandler.
/// All dependencies mocked with NSubstitute — pure logic verification.
/// </summary>
public sealed class CreateRuleSetCommandHandlerTests
{
    private readonly IRuleSetRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<CreateRuleSetCommandHandler> _logger;
    private readonly CreateRuleSetCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public CreateRuleSetCommandHandlerTests()
    {
        _repository = Substitute.For<IRuleSetRepository>();
        _currentUser = Substitute.For<ICurrentUser>();
        _eventPublisher = Substitute.For<IDomainEventPublisher>();
        _tenantContext = Substitute.For<ITenantContext>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _logger = Substitute.For<ILogger<CreateRuleSetCommandHandler>>();

        _currentUser.UserId.Returns(TestUserId);
        _tenantContext.TenantId.Returns(TestTenantId);
        _guidGenerator.NewIdMonotonic().Returns(Guid.NewGuid());

        _handler = new CreateRuleSetCommandHandler(
            _repository, _currentUser, _eventPublisher, _tenantContext, _guidGenerator, _logger);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsDtoWithExpectedName()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.Name.Should().Be("Invoice Rules");
    }

    [Fact]
    public async Task Handle_ValidCommand_StatusIsDraft()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task Handle_ValidCommand_TenantIdMatches()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.TenantId.Should().Be(TestTenantId);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsRuleSet()
    {
        await _handler.Handle(BuildCommand(), CancellationToken.None);
        await _repository.Received(1).AddAsync(
            Arg.Is<RuleSet>(rs => rs.Name == "Invoice Rules" && rs.TenantId == TestTenantId),
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
    public async Task Handle_ValidCommand_OptionalFieldsAreSet()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);
        result.Value.Category.Should().Be("Finance");
        result.Value.TargetEntityType.Should().Be("Invoice");
    }

    // --- Repository failure ---

    [Fact]
    public async Task Handle_RepositoryThrows_ExceptionPropagates()
    {
        _repository.AddAsync(Arg.Any<RuleSet>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        var act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }

    // --- Cross-tenant guard ---

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: context tenant = TenantA, command.TenantId = TenantB
        var differentTenantId = Guid.NewGuid();
        var command = new CreateRuleSetCommand(differentTenantId, "Invoice Rules", "Finance rules", "Finance", "Invoice");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("Cross-tenant");
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Cross-tenant")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // --- Helpers ---

    private static CreateRuleSetCommand BuildCommand() =>
        new(TestTenantId, "Invoice Rules", "Finance rules", "Finance", "Invoice");
}
