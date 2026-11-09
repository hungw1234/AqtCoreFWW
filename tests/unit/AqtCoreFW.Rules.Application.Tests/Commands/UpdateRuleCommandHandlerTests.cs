using AqtCoreFW.Rules.Application.Commands.UpdateRule;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Commands;

public sealed class UpdateRuleCommandHandlerTests
{
    private readonly IRuleSetRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IRuleEvaluator _ruleEvaluator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UpdateRuleCommandHandler> _logger;
    private readonly UpdateRuleCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public UpdateRuleCommandHandlerTests()
    {
        _repository = Substitute.For<IRuleSetRepository>();
        _currentUser = Substitute.For<ICurrentUser>();
        _ruleEvaluator = Substitute.For<IRuleEvaluator>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<UpdateRuleCommandHandler>>();

        _currentUser.UserId.Returns(TestUserId);
        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _handler = new UpdateRuleCommandHandler(
            _repository, _currentUser, _ruleEvaluator, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_ExistingRule_ReturnsSuccess()
    {
        var (ruleSet, rulePublicId) = BuildRuleSetWithRule();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var result = await _handler.Handle(
            new UpdateRuleCommand(ruleSet.PublicId, rulePublicId, "Renamed", null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingRule_PersistsUpdate()
    {
        var (ruleSet, rulePublicId) = BuildRuleSetWithRule();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        await _handler.Handle(
            new UpdateRuleCommand(ruleSet.PublicId, rulePublicId, "Renamed", null, null, null, null),
            CancellationToken.None);

        await _repository.Received(1).UpdateAsync(Arg.Any<RuleSet>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingRule_InvalidatesCompiledEngineCache()
    {
        var (ruleSet, rulePublicId) = BuildRuleSetWithRule();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        await _handler.Handle(
            new UpdateRuleCommand(ruleSet.PublicId, rulePublicId, "Renamed", null, null, null, null),
            CancellationToken.None);

        // IRuleEvaluator.InvalidateCache is keyed by RuleSet.PublicId (Guid AK).
        _ruleEvaluator.Received(1).InvalidateCache(ruleSet.PublicId);
    }

    [Fact]
    public async Task Handle_RuleSetNotFound_ReturnsFailure()
    {
        _repository.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RuleSet?)null);

        var result = await _handler.Handle(
            new UpdateRuleCommand(Guid.NewGuid(), Guid.NewGuid(), "X", null, null, null, null),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RuleSetNotFound_DoesNotInvalidateCache()
    {
        _repository.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RuleSet?)null);

        await _handler.Handle(
            new UpdateRuleCommand(Guid.NewGuid(), Guid.NewGuid(), "X", null, null, null, null),
            CancellationToken.None);

        _ruleEvaluator.DidNotReceive().InvalidateCache(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_RuleNotFoundInSet_DoesNotInvalidateCache()
    {
        var (ruleSet, _) = BuildRuleSetWithRule();
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var result = await _handler.Handle(
            new UpdateRuleCommand(ruleSet.PublicId, Guid.NewGuid(), "X", null, null, null, null),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        _ruleEvaluator.DidNotReceive().InvalidateCache(Arg.Any<Guid>());
    }

    // --- Cross-tenant guard (belt+suspenders — tests dead-path when repo filter bypassed) ---

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: context tenant = TenantA, seeded RuleSet.TenantId = TenantB
        var differentTenantId = Guid.NewGuid();
        var (ruleSet, rulePublicId) = BuildRuleSetWithRule(differentTenantId);
        _repository.GetByPublicIdAsync(ruleSet.PublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        // Act
        var result = await _handler.Handle(
            new UpdateRuleCommand(ruleSet.PublicId, rulePublicId, "Renamed", null, null, null, null),
            CancellationToken.None);

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

    private static (RuleSet RuleSet, Guid RulePublicId) BuildRuleSetWithRule() =>
        BuildRuleSetWithRule(TestTenantId);

    /// <summary>
    /// Creates a RuleSet + child Rule using IGuidGenerator mocks so PublicIds are deterministic.
    /// T2 entities: RuleSet.PublicId and Rule.PublicId are set by IGuidGenerator at factory time.
    /// </summary>
    private static (RuleSet RuleSet, Guid RulePublicId) BuildRuleSetWithRule(Guid tenantId)
    {
        var rsGen = Substitute.For<IGuidGenerator>();
        rsGen.NewIdMonotonic().Returns(Guid.NewGuid());
        var rs = RuleSet.Create(rsGen, "Test", tenantId, TestUserId);

        var rulePublicId = Guid.NewGuid();
        var ruleGen = Substitute.For<IGuidGenerator>();
        ruleGen.NewIdMonotonic().Returns(rulePublicId);
        // T1 FK: rs.Id is 0 in unit scope (IDENTITY assigned by EF). Long FK is fine for in-memory lookup.
        var rule = Rule.Create(ruleGen, rs.Id, "OriginalName", "{}", TestUserId,
            priority: 0, enabled: true, errorMessage: null,
            failureAction: FailureAction.Skip);
        rs.AddRule(rule);
        return (rs, rulePublicId);
    }
}
