using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetAgingReportQueryHandler.
/// Verifies: happy path, cache hit, tenant isolation, optional definitionId filter.
/// </summary>
public sealed class GetAgingReportQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly GetAgingReportQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid DefId = Guid.NewGuid();

    public GetAgingReportQueryHandlerTests()
    {
        _handler = new GetAgingReportQueryHandler(_db, _cache);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesDbAndCaches()
    {
        var rows = new List<AgingInstanceDto>
        {
            new()
            {
                InstanceId = Guid.NewGuid(),
                DefinitionId = DefId,
                StateName = "InReview",
                HoursInState = 72.5,
                EnteredAt = DateTimeOffset.UtcNow.AddHours(-72.5)
            }
        };
        _cache.GetAsync<List<AgingInstanceDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<AgingInstanceDto>?)null);
        _db.QueryAsync<AgingInstanceDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _handler.Handle(
            new GetAgingReportQuery(TenantA, MinHoursInState: 24), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].HoursInState.Should().Be(72.5);
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<List<AgingInstanceDto>>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsWithoutDbCall()
    {
        var cached = new List<AgingInstanceDto>
        {
            new() { InstanceId = Guid.NewGuid(), DefinitionId = DefId, StateName = "Pending", HoursInState = 48, EnteredAt = DateTimeOffset.UtcNow.AddHours(-48) }
        };
        _cache.GetAsync<List<AgingInstanceDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _handler.Handle(
            new GetAgingReportQuery(TenantA, MinHoursInState: 24), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _db.DidNotReceive().QueryAsync<AgingInstanceDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIsolation_DifferentTenantsGetDifferentCacheKeys()
    {
        _cache.GetAsync<List<AgingInstanceDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<AgingInstanceDto>?)null);
        _db.QueryAsync<AgingInstanceDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgingInstanceDto>());

        await _handler.Handle(new GetAgingReportQuery(TenantA, 24), CancellationToken.None);
        await _handler.Handle(new GetAgingReportQuery(TenantB, 24), CancellationToken.None);

        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantA.ToString())),
            Arg.Any<List<AgingInstanceDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantB.ToString())),
            Arg.Any<List<AgingInstanceDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDefinitionId_PassesFilterThroughCacheKey()
    {
        _cache.GetAsync<List<AgingInstanceDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<AgingInstanceDto>?)null);
        _db.QueryAsync<AgingInstanceDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgingInstanceDto>());

        await _handler.Handle(new GetAgingReportQuery(TenantA, 24, DefId), CancellationToken.None);

        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(DefId.ToString())),
            Arg.Any<List<AgingInstanceDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }
}
