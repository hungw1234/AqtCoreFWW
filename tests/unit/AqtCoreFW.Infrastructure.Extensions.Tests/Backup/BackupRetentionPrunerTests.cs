using AqtCoreFW.Infrastructure.Backup;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Backup;

/// <summary>
/// Unit tests for <see cref="BackupRetentionPruner.SelectExpired"/>. Pure (no I/O, no DB) —
/// exercises the retention cutoff boundary. Records are built via the real
/// <see cref="BackupRecord.Create"/> factory; because <c>CompletedAt</c> has a private setter
/// (set only by <c>MarkCompleted</c> to "now"), the cutoff is driven by shifting the
/// <c>now</c>/<c>retentionDays</c> arguments rather than by fabricating arbitrary timestamps.
/// </summary>
public sealed class BackupRetentionPrunerTests
{
    private static readonly IGuidGenerator Guids = BuildGuidGenerator();

    private static IGuidGenerator BuildGuidGenerator()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    /// <summary>A Completed backup whose CompletedAt ≈ now (set by MarkCompleted).</summary>
    private static BackupRecord CompletedBackup()
    {
        var record = BackupRecord.Create(Guids, "ScheduledBackup", Guid.Empty);
        record.MarkCompleted("/backups/test.bak", 1024);
        return record;
    }

    [Fact]
    public void SelectExpired_CompletedBeforeCutoff_IsSelected()
    {
        var record = CompletedBackup(); // CompletedAt ≈ now

        // Cutoff = (now + 31d) - 30d = now + 1d → record (now) is strictly older → selected.
        var expired = BackupRetentionPruner.SelectExpired(
            new[] { record }, DateTimeOffset.UtcNow.AddDays(31), retentionDays: 30);

        expired.Should().ContainSingle().Which.Should().BeSameAs(record);
    }

    [Fact]
    public void SelectExpired_CompletedWithinRetention_IsNotSelected()
    {
        var record = CompletedBackup(); // CompletedAt ≈ now

        // Cutoff = now - 30d → record (now) is newer than cutoff → kept.
        var expired = BackupRetentionPruner.SelectExpired(
            new[] { record }, DateTimeOffset.UtcNow, retentionDays: 30);

        expired.Should().BeEmpty();
    }

    [Fact]
    public void SelectExpired_ExactlyAtCutoff_IsNotSelected()
    {
        var record = CompletedBackup();
        var completedAt = record.CompletedAt!.Value;

        // now chosen so cutoff == completedAt exactly; predicate is strict `<` → not selected.
        var now = completedAt.AddDays(30);
        var expired = BackupRetentionPruner.SelectExpired(
            new[] { record }, now, retentionDays: 30);

        expired.Should().BeEmpty("the boundary uses strict less-than, so a record exactly at the cutoff is retained");
    }

    [Fact]
    public void SelectExpired_NullCompletedAt_IsNeverSelected()
    {
        // A Pending/InProgress record was never marked → CompletedAt is null, so it has no
        // retention anchor. Even with an absurdly old cutoff it must not be pruned.
        var pending = BackupRecord.Create(Guids, "ScheduledBackup", Guid.Empty);

        var expired = BackupRetentionPruner.SelectExpired(
            new[] { pending }, DateTimeOffset.UtcNow.AddDays(3650), retentionDays: 30);

        expired.Should().BeEmpty("a record with no CompletedAt has no retention anchor and must never be pruned");
    }

    [Fact]
    public void SelectExpired_EmptyInput_ReturnsEmpty()
    {
        var expired = BackupRetentionPruner.SelectExpired(
            Array.Empty<BackupRecord>(), DateTimeOffset.UtcNow, retentionDays: 30);

        expired.Should().BeEmpty();
    }

    [Fact]
    public void SelectExpired_CustomRetentionDays_ShiftsCutoff()
    {
        var record = CompletedBackup(); // CompletedAt ≈ now

        // retentionDays: 7 → cutoff = (now + 8d) - 7d = now + 1d → selected.
        var expired = BackupRetentionPruner.SelectExpired(
            new[] { record }, DateTimeOffset.UtcNow.AddDays(8), retentionDays: 7);

        expired.Should().ContainSingle();
    }
}
