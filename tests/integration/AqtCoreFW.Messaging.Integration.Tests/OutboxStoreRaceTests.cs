using AqtCoreFW.Messaging.Integration.Tests.Fixtures;
using FluentAssertions;

namespace AqtCoreFW.Messaging.Integration.Tests;

/// <summary>
/// OUTBOX-1-DISP (v2.133p12) race coverage. Empirical proof that
/// concurrent ClaimBatchAsync calls partition row sets via single-statement CTE.
/// </summary>
[Collection(OutboxStoreCollection.CollectionName)]
public sealed class OutboxStoreRaceTests
{
    private readonly OutboxStoreFixture _fx;

    public OutboxStoreRaceTests(OutboxStoreFixture fx) => _fx = fx;

    [Fact]
    public async Task ConcurrentClaim_TwentyRows_ReturnsDisjointSets()
    {
        await _fx.TruncateAsync();
        await _fx.SeedRowsAsync(20);

        var taskA = Task.Run(() => _fx.Store.ClaimBatchAsync(_fx.Schema, 20, CancellationToken.None));
        var taskB = Task.Run(() => _fx.Store.ClaimBatchAsync(_fx.Schema, 20, CancellationToken.None));

        var results = await Task.WhenAll(taskA, taskB);
        var idsA = results[0].Select(r => r.Id).ToHashSet();
        var idsB = results[1].Select(r => r.Id).ToHashSet();

        idsA.Intersect(idsB).Should().BeEmpty(
            "atomic CTE claim must partition rows — no double-claim under concurrency");
        (idsA.Count + idsB.Count).Should().Be(20,
            "every seeded row must be claimed exactly once");
    }

    [Fact]
    public async Task StaleClaim_BeyondClaimTimeout_IsReclaimed()
    {
        await _fx.TruncateAsync();
        await _fx.SeedRowsAsync(1);

        // First claim
        var first = await _fx.Store.ClaimBatchAsync(_fx.Schema, 10, CancellationToken.None);
        first.Should().HaveCount(1);

        // Force ClaimedAtUtc back 10 minutes (default ClaimTimeout = 5 min)
        await _fx.ForceClaimedAtAsync(first[0].Id, DateTimeOffset.UtcNow.AddMinutes(-10));

        // Second claim should reclaim the same row
        var second = await _fx.Store.ClaimBatchAsync(_fx.Schema, 10, CancellationToken.None);
        second.Should().HaveCount(1);
        second[0].Id.Should().Be(first[0].Id);
    }

    [Fact]
    public async Task FreshClaim_BeforeClaimTimeout_IsNotReclaimable()
    {
        await _fx.TruncateAsync();
        await _fx.SeedRowsAsync(1);

        var first = await _fx.Store.ClaimBatchAsync(_fx.Schema, 10, CancellationToken.None);
        first.Should().HaveCount(1);

        // Immediately try to claim again — should return empty (claim is fresh)
        var second = await _fx.Store.ClaimBatchAsync(_fx.Schema, 10, CancellationToken.None);
        second.Should().BeEmpty(
            "fresh claims (ClaimedAtUtc < ClaimTimeout ago) must be excluded");
    }
}
