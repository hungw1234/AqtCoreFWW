using AqtCoreFW.Rules.Application.Commands.RunTestCase;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Tests.Commands;

/// <summary>
/// Unit tests for RunTestCaseCommandHandler tenant cross-check (FU5.1).
/// Post-fetch guard: loads TestCase → loads parent RuleSet by InternalId → checks RuleSet.TenantId.
/// </summary>
public sealed class RunTestCaseCommandHandlerTests
{
    private readonly IRuleTestCaseRepository _testCaseRepository;
    private readonly IRuleSetRepository _ruleSetRepository;
    private readonly IRuleEvaluator _evaluator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RunTestCaseCommandHandler> _logger;
    private readonly RunTestCaseCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    // T1 entity: RuleTestCase.Id and RuleTestCase.RuleSetId are long (IDENTITY / FK to RuleSet.Id).
    private const long TestCaseId = 1L;
    private const long TestRuleSetId = 2L;

    public RunTestCaseCommandHandlerTests()
    {
        _testCaseRepository = Substitute.For<IRuleTestCaseRepository>();
        _ruleSetRepository = Substitute.For<IRuleSetRepository>();
        _evaluator = Substitute.For<IRuleEvaluator>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<RunTestCaseCommandHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _handler = new RunTestCaseCommandHandler(
            _testCaseRepository, _ruleSetRepository, _evaluator, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_TestCaseBelongsToOtherTenant_ReturnsValidationError()
    {
        // Arrange: TestCase exists but its parent RuleSet belongs to different tenant.
        // RuleTestCase.RuleSetId = TestRuleSetId (long FK).
        var testCase = RuleTestCase.Create(TestRuleSetId, "Test", "{}", "{}", TestUserId);
        _testCaseRepository.GetByIdAsync(TestCaseId, Arg.Any<CancellationToken>())
            .Returns(testCase);

        // Parent RuleSet: resolved by internal FK — use GetByInternalIdAsync.
        var ruleSet = BuildRuleSet(DifferentTenantId);
        _ruleSetRepository.GetByInternalIdAsync(TestRuleSetId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        var command = new RunTestCaseCommand(TestCaseId);

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

        // Evaluator should NOT be called
        await _evaluator.DidNotReceive().EvaluateAsync(
            Arg.Any<Guid>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SystemAdminCrossTenant_Allowed()
    {
        // Arrange: SystemAdmin can run test cases for any tenant
        _tenantContext.IsSystemAdmin.Returns(true);

        var testCase = RuleTestCase.Create(TestRuleSetId, "Test", "{}", "{}", TestUserId);
        _testCaseRepository.GetByIdAsync(TestCaseId, Arg.Any<CancellationToken>())
            .Returns(testCase);

        var ruleSet = BuildRuleSet(DifferentTenantId);
        _ruleSetRepository.GetByInternalIdAsync(TestRuleSetId, Arg.Any<CancellationToken>())
            .Returns(ruleSet);

        _evaluator.EvaluateAsync(Arg.Any<Guid>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new RuleEvalDomainResult(true, []));

        var command = new RunTestCaseCommand(TestCaseId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: Success proves guard was bypassed.
        // IRuleEvaluator.EvaluateAsync is keyed by RuleSet.PublicId (Guid AK, cross-module exposed).
        result.IsSuccess.Should().BeTrue();
        await _evaluator.Received(1).EvaluateAsync(
            ruleSet.PublicId, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    /// <summary>
    /// Builds a RuleSet using IGuidGenerator mock so PublicId is deterministic.
    /// T2 entity: PublicId set at factory time by IGuidGenerator.
    /// </summary>
    private static RuleSet BuildRuleSet(Guid tenantId)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return RuleSet.Create(gen, "Test RuleSet", tenantId, TestUserId);
    }
}
