using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Messaging.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Messaging.Integration.Tests;

/// <summary>
/// Integration coverage for <see cref="ProcessedMessageStore"/> against the real
/// <c>messaging.processed_messages</c> table + its UNIQUE (ConsumerName, DedupKey) index (Slice 1C).
/// Proves the claim primitive at the DB level: first claim wins, a duplicate is refused, and the key
/// is scoped per-consumer. Each claim runs in its own scope — mirrors the per-message consume scope.
/// </summary>
[Collection(MessagingCollection.CollectionName)]
public sealed class ProcessedMessageStoreTests(MessagingFixture fixture)
{
    private async Task<bool> ClaimAsync(string consumer, string key)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = new ProcessedMessageStore(
            scope.ServiceProvider.GetRequiredService<MessagingDbContext>());
        return await store.TryClaimAsync(consumer, key);
    }

    [Fact]
    public async Task TryClaim_FirstWins_DuplicateRefused_ScopedPerConsumerAndKey()
    {
        // Unique per test run so parallel/rerun executions never collide on the shared DB.
        var consumer = $"TestConsumer_{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();

        (await ClaimAsync(consumer, key))
            .Should().BeTrue("the first claim of a (consumer, key) pair succeeds");

        (await ClaimAsync(consumer, key))
            .Should().BeFalse("a duplicate delivery of the same (consumer, key) is refused by the unique index");

        (await ClaimAsync($"{consumer}_other", key))
            .Should().BeTrue("a different consumer handling the same message gets its own first claim");

        (await ClaimAsync(consumer, $"{key}_other"))
            .Should().BeTrue("the same consumer handling a different message gets a fresh claim");
    }
}
