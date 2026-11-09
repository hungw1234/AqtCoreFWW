using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetStateDistributionQueryHandler.
/// Verifies: happy path, empty result, cache hit (no DB call), tenant isolation.
/// </summary>
public sealed class GetStateDistributionQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly GetStateDistributionQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid DefId = Guid.NewGuid();
    private const long StateId = 1L;

    public GetStateDistributionQueryHandlerTests()
    {
        _handler = new GetStateDistributionQueryHandler(_db, _cache);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesDbAndCaches()
    {
        var rows = new List<StateDistributionDto>
        {
            new() { StateId = StateId, StateName = "InReview", Count = 5, Percentage = 100m }
        };
        _cache.GetAsync<List<StateDistributionDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<StateDistributionDto>?)null);
        _db.QueryAsync<StateDistributionDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _handler.Handle(new GetStateDistributionQuery(DefId, TenantA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].StateName.Should().Be("InReview");
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<List<StateDistributionDto>>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsWithoutDbCall()
    {
        var cached = new List<StateDistributionDto>
        {
            new() { StateId = StateId, StateName = "Draft", Count = 3, Percentage = 100m }
        };
        _cache.GetAsync<List<StateDistributionDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _handler.Handle(new GetStateDistributionQuery(DefId, TenantA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        await _db.DidNotReceive().QueryAsync<StateDistributionDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIsolation_CacheKeyIncludesTenantId()
    {
        _cache.GetAsync<List<StateDistributionDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<StateDistributionDto>?)null);
        _db.QueryAsync<StateDistributionDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<StateDistributionDto>());

        await _handler.Handle(new GetStateDistributionQuery(DefId, TenantA), CancellationToken.None);
        await _handler.Handle(new GetStateDistributionQuery(DefId, TenantB), CancellationToken.None);

        // Both tenants should cache under different keys
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantA.ToString())),
            Arg.Any<List<StateDistributionDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantB.ToString())),
            Arg.Any<List<StateDistributionDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsSuccessEmptyList()
    {
        _cache.GetAsync<List<StateDistributionDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<StateDistributionDto>?)null);
        _db.QueryAsync<StateDistributionDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<StateDistributionDto>());

        var result = await _handler.Handle(new GetStateDistributionQuery(DefId, TenantA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
