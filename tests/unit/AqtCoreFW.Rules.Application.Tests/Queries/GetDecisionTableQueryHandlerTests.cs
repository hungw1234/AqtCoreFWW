using AqtCoreFW.Rules.Application.Queries.GetDecisionTable;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetDecisionTableQueryHandler tenant cross-check (FU5.1).
/// </summary>
public sealed class GetDecisionTableQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<GetDecisionTableQueryHandler> _logger;
    private readonly GetDecisionTableQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();

    // T1 entity: DecisionTable.Id is long (IDENTITY assigned by EF).
    private const long TestTableId = 1L;

    public GetDecisionTableQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<GetDecisionTableQueryHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _handler = new GetDecisionTableQueryHandler(_db, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: request.TenantId != tenantContext.TenantId (non-SystemAdmin)
        var query = new GetDecisionTableQuery(TestTableId, DifferentTenantId);

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
        // SystemAdmin bypasses guard; unmocked Dapper generic returns null → NotFoundError proves guard-bypass.
        _tenantContext.IsSystemAdmin.Returns(true);
        var query = new GetDecisionTableQuery(TestTableId, DifferentTenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }
}
