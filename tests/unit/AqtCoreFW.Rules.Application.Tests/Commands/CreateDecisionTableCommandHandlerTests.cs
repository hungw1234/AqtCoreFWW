using AqtCoreFW.Rules.Application.Commands.CreateDecisionTable;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateDecisionTableCommandHandler.
/// All dependencies mocked with NSubstitute — pure logic verification.
/// </summary>
public sealed class CreateDecisionTableCommandHandlerTests
{
    private readonly IDecisionTableRepository _repository;
    private readonly IRuleSetRepository _ruleSetRepository;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CreateDecisionTableCommandHandler> _logger;
    private readonly CreateDecisionTableCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestRuleSetPublicId = Guid.NewGuid();

    public CreateDecisionTableCommandHandlerTests()
    {
        _repository = Substitute.For<IDecisionTableRepository>();
        _ruleSetRepository = Substitute.For<IRuleSetRepository>();
        _currentUser = Substitute.For<ICurrentUser>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<CreateDecisionTableCommandHandler>>();

        _currentUser.UserId.Returns(TestUserId);
        _tenantContext.TenantId.Returns(TestTenantId);

        _handler = new CreateDecisionTableCommandHandler(
            _repository, _ruleSetRepository, _currentUser, _tenantContext, _logger);
    }

    // --- Cross-tenant guard ---

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: context tenant = TestTenantId, command.TenantId = different tenant
        var differentTenantId = Guid.NewGuid();
        var command = new CreateDecisionTableCommand(
            TestRuleSetPublicId, differentTenantId, "Test Table", "[]", "[]", "[]");

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

    // --- Happy path ---

    [Fact]
    public async Task Handle_SameTenant_Succeeds()
    {
        // Arrange: resolve RuleSet by PublicId — returns a valid parent RuleSet.
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(TestRuleSetPublicId);
        var ruleSet = RuleSet.Create(gen, "Parent RuleSet", TestTenantId, TestUserId);
        _ruleSetRepository.GetByPublicIdAsync(TestRuleSetPublicId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var command = BuildCommand();

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test Table");
        result.Value.TenantId.Should().Be(TestTenantId);
        await _repository.Received(1).AddAsync(
            Arg.Is<DecisionTable>(dt => dt.Name == "Test Table" && dt.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RuleSetNotFound_ReturnsNotFoundError()
    {
        // Arrange: parent RuleSet lookup returns null
        _ruleSetRepository.GetByPublicIdAsync(TestRuleSetPublicId, Arg.Any<CancellationToken>())
            .Returns((RuleSet?)null);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    // --- Helpers ---

    private static CreateDecisionTableCommand BuildCommand() =>
        new(TestRuleSetPublicId, TestTenantId, "Test Table", "[]", "[]", "[]");
}
