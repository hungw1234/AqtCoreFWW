using AqtCoreFW.Rules.Application.Queries.SimulateRules;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Tests.Queries;

/// <summary>
/// Unit tests for SimulateRulesQueryHandler tenant cross-check (FU5.1).
/// </summary>
public sealed class SimulateRulesQueryHandlerTests
{
    private readonly IRuleEvaluator _evaluator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SimulateRulesQueryHandler> _logger;
    private readonly SimulateRulesQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();
    private static readonly Guid TestRuleSetId = Guid.NewGuid();

    public SimulateRulesQueryHandlerTests()
    {
        _evaluator = Substitute.For<IRuleEvaluator>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<SimulateRulesQueryHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _handler = new SimulateRulesQueryHandler(_evaluator, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: request.TenantId != tenantContext.TenantId (non-SystemAdmin)
        using var input = JsonDocument.Parse("{}");
        var query = new SimulateRulesQuery(TestRuleSetId, DifferentTenantId, input);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

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
        // Arrange: SystemAdmin can simulate rules for any tenant
        _tenantContext.IsSystemAdmin.Returns(true);
        using var input = JsonDocument.Parse("{}");
        var query = new SimulateRulesQuery(TestRuleSetId, DifferentTenantId, input);

        _evaluator.EvaluateAsync(Arg.Any<Guid>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new RuleEvalDomainResult(true, []));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert: Success proves guard was bypassed
        result.IsSuccess.Should().BeTrue();
        await _evaluator.Received(1).EvaluateAsync(
            TestRuleSetId, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }
}
