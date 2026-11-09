using AqtCoreFW.Rules.Application.Queries.ListRuleSets;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Queries;

/// <summary>
/// Unit tests for ListRuleSetsQueryHandler tenant cross-check (FU5.1).
/// </summary>
public sealed class ListRuleSetsQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ListRuleSetsQueryHandler> _logger;
    private readonly ListRuleSetsQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();

    public ListRuleSetsQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<ListRuleSetsQueryHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _handler = new ListRuleSetsQueryHandler(_db, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: request.TenantId != tenantContext.TenantId (non-SystemAdmin)
        var query = new ListRuleSetsQuery(DifferentTenantId);

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

    [Fact]
    public async Task Handle_SystemAdminCrossTenant_Allowed()
    {
        // Arrange: SystemAdmin can access any tenant's data
        _tenantContext.IsSystemAdmin.Returns(true);
        var query = new ListRuleSetsQuery(DifferentTenantId);

        // Dapper returns empty list and count=0
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _db.QueryAsync<object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<object>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert: Success with empty list proves guard was bypassed
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
    }
}
