using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.SharedKernel.Application.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authorization;

/// <summary>
/// Unit tests for AbacCacheRefreshService (T4-05-FU).
/// Verify: (1) refresh evicts both ABAC key prefixes, (2) exception propagates from
/// RefreshAsync so the BackgroundService loop's error handler can log and continue.
/// </summary>
public sealed class AbacCacheRefreshServiceTests
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ILogger<AbacCacheRefreshService> _logger = Substitute.For<ILogger<AbacCacheRefreshService>>();
    private readonly AbacCacheRefreshService _sut;

    public AbacCacheRefreshServiceTests()
    {
        _sut = new AbacCacheRefreshService(_cache, _logger);
    }

    [Fact]
    public async Task RefreshAsync_EvictsBothSystemAndTenantPrefixes()
    {
        await _sut.RefreshAsync(CancellationToken.None);

        await _cache.Received(1).RemoveByPrefixAsync(
            Arg.Is<string>(p => p == "sys:abac:"),
            Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            Arg.Is<string>(p => p == "t:*:abac:"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_PropagatesCacheException_ForLoopToHandle()
    {
        // RefreshAsync deliberately does NOT swallow — ExecuteAsync's try/catch logs and
        // continues to the next tick. Guarantees a transient Redis blip doesn't silently
        // skip eviction without an error log.
        _cache.RemoveByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("redis down"));

        Func<Task> act = () => _sut.RefreshAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("redis down");
    }
}
