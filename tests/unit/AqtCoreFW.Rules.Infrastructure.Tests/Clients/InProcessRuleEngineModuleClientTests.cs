using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.Rules.Infrastructure.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace AqtCoreFW.Rules.Infrastructure.Tests.Clients;

/// <summary>
/// Unit tests for InProcessRuleEngineModuleClient tenant cross-check (FU5.1).
/// Internal invariant breach throws InvalidOperationException (HTTP 500 signal).
/// </summary>
public sealed class InProcessRuleEngineModuleClientTests
{
    private readonly IRuleEvaluator _evaluator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<InProcessRuleEngineModuleClient> _logger;
    private readonly InProcessRuleEngineModuleClient _client;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();
    private static readonly Guid TestRuleSetId = Guid.NewGuid();

    public InProcessRuleEngineModuleClientTests()
    {
        _evaluator = Substitute.For<IRuleEvaluator>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<InProcessRuleEngineModuleClient>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _client = new InProcessRuleEngineModuleClient(_evaluator, _tenantContext, _logger);
    }

    [Fact]
    public async Task EvaluateAsync_CrossTenant_ThrowsInvalidOperationException()
    {
        // Arrange: tenantId param != tenantContext.TenantId (internal invariant breach)
        using var input = JsonDocument.Parse("{}");

        // Act
        var act = () => _client.EvaluateAsync(TestRuleSetId, input, DifferentTenantId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");

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
    public async Task EvaluateAsync_SystemAdminCrossTenant_Allowed()
    {
        // Arrange: SystemAdmin can evaluate rules for any tenant
        _tenantContext.IsSystemAdmin.Returns(true);
        using var input = JsonDocument.Parse("{}");

        _evaluator.EvaluateAsync(Arg.Any<Guid>(), Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>())
            .Returns(new RuleEvalDomainResult(true, []));

        // Act
        var result = await _client.EvaluateAsync(TestRuleSetId, input, DifferentTenantId);

        // Assert: Success proves guard was bypassed
        result.IsSuccess.Should().BeTrue();
        await _evaluator.Received(1).EvaluateAsync(
            TestRuleSetId, Arg.Any<JsonDocument>(), Arg.Any<CancellationToken>());
    }
}
