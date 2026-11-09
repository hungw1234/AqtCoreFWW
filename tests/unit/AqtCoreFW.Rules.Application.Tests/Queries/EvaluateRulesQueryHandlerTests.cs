using AqtCoreFW.Rules.Application.Queries.EvaluateRules;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Tests.Queries;

/// <summary>
/// Unit tests for EvaluateRulesQueryHandler tenant cross-check (FU5.1).
/// Guards against audit-event spoof via request.TenantId manipulation.
/// </summary>
public sealed class EvaluateRulesQueryHandlerTests
{
    private readonly IRuleEvaluator _evaluator;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EvaluateRulesQueryHandler> _logger;
    private readonly EvaluateRulesQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();
    private static readonly Guid TestRuleSetId = Guid.NewGuid();

    public EvaluateRulesQueryHandlerTests()
    {
        _evaluator = Substitute.For<IRuleEvaluator>();
        _eventPublisher = Substitute.For<IDomainEventPublisher>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<EvaluateRulesQueryHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        var gen = Substitute.For<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new EvaluateRulesQueryHandler(_evaluator, _eventPublisher, _tenantContext, gen, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: request.TenantId != tenantContext.TenantId (audit spoof attempt)
        using var input = JsonDocument.Parse("{}");
        var query = new EvaluateRulesQuery(TestRuleSetId, DifferentTenantId, input);

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
        // Arrange: SystemAdmin can evaluate rules for any tenant
        _tenantContext.IsSystemAdmin.Returns(true);
        using var input = JsonDocument.Parse("{}");
        var query = new EvaluateRulesQuery(TestRuleSetId, DifferentTenantId, input);

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
