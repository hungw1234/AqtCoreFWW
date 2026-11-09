using AqtCoreFW.Rules.Application.Queries.GetRuleSet;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Rules.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetRuleSetQueryHandler tenant cross-check (FU5.1).
/// </summary>
public sealed class GetRuleSetQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<GetRuleSetQueryHandler> _logger;
    private readonly GetRuleSetQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid DifferentTenantId = Guid.NewGuid();
    private static readonly Guid TestRuleSetId = Guid.NewGuid();

    public GetRuleSetQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<GetRuleSetQueryHandler>>();

        _tenantContext.TenantId.Returns(TestTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);

        _handler = new GetRuleSetQueryHandler(_db, _tenantContext, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_ReturnsValidationError()
    {
        // Arrange: request.TenantId != tenantContext.TenantId (non-SystemAdmin)
        var query = new GetRuleSetQuery(TestRuleSetId, DifferentTenantId);

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
        var query = new GetRuleSetQuery(TestRuleSetId, DifferentTenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_NullContextTenant_ReturnsValidationError()
    {
        // R3 red-team commitment: non-SystemAdmin with null tenant context must block cross-tenant attempt.
        _tenantContext.TenantId.Returns((Guid?)null);
        var query = new GetRuleSetQuery(TestRuleSetId, DifferentTenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
    }
}
