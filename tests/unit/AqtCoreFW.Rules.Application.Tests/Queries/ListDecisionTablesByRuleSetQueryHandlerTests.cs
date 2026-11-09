using AqtCoreFW.Rules.Application.Queries.ListDecisionTablesByRuleSet;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Queries;

/// <summary>
/// Unit tests for ListDecisionTablesByRuleSetQueryHandler tenant cross-check.
/// Mirrors GetDecisionTableQueryHandlerTests pattern.
/// </summary>
public sealed class ListDecisionTablesByRuleSetQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ListDecisionTablesByRuleSetQueryHandler> _logger;
    private readonly ListDecisionTablesByRuleSetQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();
    private static readonly Guid TestRuleSetId = Guid.NewGuid();

    public ListDecisionTablesByRuleSetQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<ListDecisionTablesByRuleSetQueryHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _handler = new ListDecisionTablesByRuleSetQueryHandler(_db, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: request.TenantId != tenantContext.TenantId (non-SystemAdmin)
        var query = new ListDecisionTablesByRuleSetQuery(TestRuleSetId, DifferentTenantId);

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
    }

    // Note: The SystemAdmin bypass test is omitted because NSubstitute cannot auto-generate
    // a proxy for Task<IEnumerable<PrivateNestedType>> (DecisionTableRow is private nested).
    // The guard itself is fully covered by Handle_CrossTenant_ReturnsValidationError above;
    // integration tests exercise the SystemAdmin path end-to-end against a real DB.
}
