using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Messaging.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Messaging.Integration.Tests;

/// <summary>
/// Integration coverage for <see cref="ProcessedMessageCleanupJob.RunOnceAsync"/> against the real
/// <c>messaging.processed_messages</c> table (Slice 1C). Guards the load-bearing retention predicate:
/// a flipped comparison would delete LIVE dedup keys and silently re-enable double-effects.
/// </summary>
[Collection(MessagingCollection.CollectionName)]
public sealed class ProcessedMessageCleanupJobTests(MessagingFixture fixture)
{
    [Fact]
    public async Task RunOnce_DeletesRowsPastRetention_KeepsFreshRows()
    {
        const string consumer = "CleanupTestConsumer";
        var expiredKey = $"expired_{Guid.NewGuid():N}";
        var freshKey   = $"fresh_{Guid.NewGuid():N}";

        // Seed one row well past the 30-day window and one just recorded.
        await using (var seed = fixture.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<MessagingDbContext>();
            db.ProcessedMessages.Add(new ProcessedMessage
            {
                ConsumerName = consumer, DedupKey = expiredKey,
                ProcessedAtUtc = DateTimeOffset.UtcNow.AddDays(-40),
            });
            db.ProcessedMessages.Add(new ProcessedMessage
            {
                ConsumerName = consumer, DedupKey = freshKey,
                ProcessedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var job = new ProcessedMessageCleanupJob(
            fixture.Services, NullLogger<ProcessedMessageCleanupJob>.Instance);
        var deleted = await job.RunOnceAsync();

        deleted.Should().BeGreaterThanOrEqualTo(1);

        await using var verify = fixture.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<MessagingDbContext>();
        (await vdb.ProcessedMessages.AnyAsync(m => m.DedupKey == expiredKey))
            .Should().BeFalse("a row older than the retention window must be swept");
        (await vdb.ProcessedMessages.AnyAsync(m => m.DedupKey == freshKey))
            .Should().BeTrue("a fresh row is a live dedup key and must survive");
    }
}
