using AqtCoreFW.Rules.Application.Commands.ActivateRuleVersion;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Commands;

public sealed class ActivateRuleVersionCommandHandlerTests
{
    private readonly IRuleSetRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IRuleEvaluator _ruleEvaluator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ActivateRuleVersionCommandHandler> _logger;
    private readonly ActivateRuleVersionCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public ActivateRuleVersionCommandHandlerTests()
    {
        _repository = Substitute.For<IRuleSetRepository>();
        _currentUser = Substitute.For<ICurrentUser>();
        _eventPublisher = Substitute.For<IDomainEventPublisher>();
        _ruleEvaluator = Substitute.For<IRuleEvaluator>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<ActivateRuleVersionCommandHandler>>();

        _currentUser.UserId.Returns(TestUserId);
        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        var gen = Substitute.For<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new ActivateRuleVersionCommandHandler(
            _repository, _currentUser, _eventPublisher, _ruleEvaluator, _tenantContext, gen, _logger);
    }

    [Fact]
    public async Task Handle_ExistingRuleSet_ReturnsSuccess()
    {
        var ruleSet = BuildRuleSet();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var result = await _handler.Handle(
            new ActivateRuleVersionCommand(ruleSet.PublicId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingRuleSet_StatusBecomesActive()
    {
        var ruleSet = BuildRuleSet();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var result = await _handler.Handle(
            new ActivateRuleVersionCommand(ruleSet.PublicId), CancellationToken.None);
        result.Value.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_ExistingRuleSet_VersionIncrements()
    {
        var ruleSet = BuildRuleSet(); // starts at Version=1
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var result = await _handler.Handle(
            new ActivateRuleVersionCommand(ruleSet.PublicId), CancellationToken.None);
        result.Value.Version.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ExistingRuleSet_PersistsUpdate()
    {
        var ruleSet = BuildRuleSet();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        await _handler.Handle(
            new ActivateRuleVersionCommand(ruleSet.PublicId), CancellationToken.None);
        await _repository.Received(1).UpdateAsync(Arg.Any<RuleSet>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailure()
    {
        _repository.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RuleSet?)null);

        var result = await _handler.Handle(
            new ActivateRuleVersionCommand(Guid.NewGuid()), CancellationToken.None);
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_ExistingRuleSet_PublishesDomainEvent()
    {
        var ruleSet = BuildRuleSet();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        await _handler.Handle(
            new ActivateRuleVersionCommand(ruleSet.PublicId), CancellationToken.None);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Any<IEnumerable<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    // --- Cross-tenant guard (belt+suspenders — tests dead-path when repo filter bypassed) ---

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: context tenant = TenantA, seeded RuleSet.TenantId = TenantB
        var differentTenantId = Guid.NewGuid();
        var ruleSet = BuildRuleSet(differentTenantId);
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        // Act
        var result = await _handler.Handle(
            new ActivateRuleVersionCommand(ruleSet.PublicId), CancellationToken.None);

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

    /// <summary>
    /// Builds a RuleSet using IGuidGenerator mock so PublicId is deterministic for test assertions.
    /// T2 entity: PublicId (Guid AK) set at factory time by IGuidGenerator.
    /// </summary>
    private static RuleSet BuildRuleSet(Guid? tenantId = null)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return RuleSet.Create(gen, "Test", tenantId ?? TestTenantId, TestUserId);
    }
}
