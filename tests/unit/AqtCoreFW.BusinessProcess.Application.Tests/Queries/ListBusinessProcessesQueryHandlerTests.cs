using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.BusinessProcess.Application.Queries.ListBusinessProcesses;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Queries;

/// <summary>
/// Unit tests for ListBusinessProcessesQueryHandler — Pattern A tenant guard at handler boundary.
/// Defence-in-depth: handler must reject cross-tenant non-SystemAdmin calls before any DB hit.
/// Mirrors Rules.ListRuleSetsQueryHandlerTests pattern (substitute IReadDbConnection for happy path).
/// </summary>
public sealed class ListBusinessProcessesQueryHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ILogger<ListBusinessProcessesQueryHandler> _logger = Substitute.For<ILogger<ListBusinessProcessesQueryHandler>>();
    private readonly ListBusinessProcessesQueryHandler _handler;

    public ListBusinessProcessesQueryHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _cache.GetAsync<PagedResult<BusinessProcessListItemDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PagedResult<BusinessProcessListItemDto>?)null);
        _handler = new ListBusinessProcessesQueryHandler(_db, _tenantContext, _cache, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_NonSystemAdmin_ReturnsValidationError()
    {
        var query = new ListBusinessProcessesQuery(OtherTenantId, Search: null, IsActive: null, Page: 1, PageSize: 20);

        var result = await _handler.Handle(query, CancellationToken.None);

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
    public async Task Handle_SystemAdminCrossTenant_BypassesGuard()
    {
        _tenantContext.IsSystemAdmin.Returns(true);
        var query = new ListBusinessProcessesQuery(OtherTenantId, Search: null, IsActive: null, Page: 1, PageSize: 20);

        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _db.QueryAsync<BusinessProcessListItemDto>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<BusinessProcessListItemDto>());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MatchingTenant_PaginationClamped()
    {
        var query = new ListBusinessProcessesQuery(CurrentTenantId, Search: null, IsActive: true, Page: 0, PageSize: 999);

        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _db.QueryAsync<BusinessProcessListItemDto>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<BusinessProcessListItemDto>());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.Page.Should().Be(1, "page<1 should be clamped to 1");
        result.Value.Meta.PageSize.Should().Be(100, "pageSize>100 should be clamped to 100");
    }
}
