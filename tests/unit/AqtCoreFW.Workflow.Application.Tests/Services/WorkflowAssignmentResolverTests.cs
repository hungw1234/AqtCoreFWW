using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Services;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Services;

/// <summary>
/// Unit tests for WorkflowAssignmentResolver.
/// Validates: cache-hit path, cache-miss path, DB query, failure when no rule found.
/// DB filtering logic (specificity, expiry, inactive) is validated via mock row returns.
/// </summary>
public sealed class WorkflowAssignmentResolverTests
{
    private readonly ICacheService _cache;
    private readonly IReadDbConnection _db;
    private readonly WorkflowAssignmentResolver _resolver;

    private static readonly Guid TenantId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid DefinitionId = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public WorkflowAssignmentResolverTests()
    {
        _cache = Substitute.For<ICacheService>();
        _db = Substitute.For<IReadDbConnection>();
        _resolver = new WorkflowAssignmentResolver(_cache, _db);
    }

    // ── Cache hit ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_CacheHit_ReturnsCachedValue()
    {
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(DefinitionId));

        var result = await _resolver.ResolveAsync(TenantId, "Case", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DefinitionId);
        // DB should NOT be queried on cache hit
        await _db.DidNotReceive().QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // ── Cache miss → DB ────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_CacheMiss_QueriesDb_CachesResult()
    {
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([DefinitionId]));

        var result = await _resolver.ResolveAsync(TenantId, "Case", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DefinitionId);

        // Result must be written to cache with 24h TTL
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Is<Guid?>(v => v == DefinitionId),
            Arg.Is<TimeSpan>(t => t == TimeSpan.FromHours(24)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_NoRuleFound_ReturnsFail()
    {
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([]));

        var result = await _resolver.ResolveAsync(TenantId, "Case", null);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("No workflow assignment rule found"));
    }

    // ── Specificity — DB returns the most specific row first (resolver trusts DB ordering) ──

    [Fact]
    public async Task Resolve_MostSpecificWins_SubTypeOverEntity()
    {
        // Simulate DB returning the TenantEntitySubType winner (SQL orders by Specificity DESC)
        var subTypeDefinitionId = Guid.NewGuid();
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([subTypeDefinitionId]));

        var result = await _resolver.ResolveAsync(TenantId, "Case", "ConstructionPermit");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(subTypeDefinitionId);
    }

    [Fact]
    public async Task Resolve_MostSpecificWins_EntityOverTenant()
    {
        var entityDefinitionId = Guid.NewGuid();
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([entityDefinitionId]));

        var result = await _resolver.ResolveAsync(TenantId, "Application", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(entityDefinitionId);
    }

    [Fact]
    public async Task Resolve_PriorityTiebreaker()
    {
        // DB returns the higher-priority winner when Specificity is equal
        var highPriorityId = Guid.NewGuid();
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([highPriorityId]));

        var result = await _resolver.ResolveAsync(TenantId, "Case", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(highPriorityId);
    }

    // ── Expired / inactive rules — DB WHERE clause excludes them ──────────────

    [Fact]
    public async Task Resolve_ExpiredRule_NotReturned()
    {
        // Simulate DB returning empty (expired rules filtered by WHERE EffectiveUntil > @Now)
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([]));

        var result = await _resolver.ResolveAsync(TenantId, "Case", null);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_InactiveRule_NotReturned()
    {
        // Simulate DB returning empty (inactive rules filtered by WHERE IsActive = 1)
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([]));

        var result = await _resolver.ResolveAsync(TenantId, "Case", null);

        result.IsFailed.Should().BeTrue();
    }

    // ── Cache key structure ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Case", null, "wf:assignment:11111111-0000-0000-0000-000000000001:Case:all")]
    [InlineData("Case", "Construction", "wf:assignment:11111111-0000-0000-0000-000000000001:Case:Construction")]
    public void BuildCacheKey_CorrectFormat(string entityType, string? subType, string expected)
    {
        var key = WorkflowAssignmentResolver.BuildCacheKey(TenantId, entityType, subType);
        key.Should().Be(expected);
    }

    [Fact]
    public async Task Resolve_PassesCorrectSqlParameters()
    {
        _cache.GetAsync<Guid?>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Guid>>([DefinitionId]));

        await _resolver.ResolveAsync(TenantId, "Case", "SubType");

        await _db.Received(1).QueryAsync<Guid>(
            Arg.Is<string>(sql =>
                sql.Contains("WorkflowAssignmentRules") &&
                sql.Contains("Specificity DESC") &&
                sql.Contains("Priority DESC")),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }
}
