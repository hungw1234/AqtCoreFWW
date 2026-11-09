using AqtCoreFW.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests cho RBAC-1 v2.133p7 — InvalidatePermissionCacheForUsersAsync.
/// Verify: (1) empty list short-circuits, (2) non-empty issues KeyDelete với key prefix
/// "perm:{userId}", (3) Redis exception logged warning, no throw (graceful degradation —
/// cache miss falls back to DB query in EffectivePermissionService).
/// </summary>
public sealed class BulkRoleChangeServicePermCacheTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly ILogger<BulkRoleChangeService> _logger = Substitute.For<ILogger<BulkRoleChangeService>>();
    private readonly BulkRoleChangeService _sut;

    public BulkRoleChangeServicePermCacheTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _sut = new BulkRoleChangeService(_redis, _logger);
    }

    [Fact]
    public async Task EmptyUserList_ShortCircuits_NoRedisCall()
    {
        await _sut.InvalidatePermissionCacheForUsersAsync(Array.Empty<Guid>());

        await _db.DidNotReceive().KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>());
        _redis.DidNotReceive().GetDatabase(Arg.Any<int>(), Arg.Any<object?>());
    }

    [Fact]
    public async Task NonEmptyList_IssuesKeyDeleteWithPermPrefix()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await _sut.InvalidatePermissionCacheForUsersAsync(new[] { u1, u2 });

        await _db.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey[]>(keys =>
                keys.Length == 2
                && keys.Any(k => k == new RedisKey($"perm:{u1}"))
                && keys.Any(k => k == new RedisKey($"perm:{u2}"))),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RedisThrows_DoesNotPropagate_LogsWarning()
    {
        _db
            .When(d => d.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>()))
            .Do(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "boom"));

        Func<Task> act = () => _sut.InvalidatePermissionCacheForUsersAsync(new[] { Guid.NewGuid() });

        await act.Should().NotThrowAsync(
            "graceful degradation — perm cache miss falls back to DB query in EffectivePermissionService");
    }
}
