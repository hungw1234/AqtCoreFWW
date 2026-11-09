using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Tests.Helpers;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// TDD tests for WorkflowVersionMigrationItem.
/// T3 entity: InternalId=long (bigint identity PK), MessageId=Guid (AK, v7 monotonic).
/// MigrationId/InstanceId/OldStateId/NewStateId: long (T1/T2 bigint PKs).
/// Covers: factory, status transitions (Succeeded/Failed/Skipped), attempt count.
/// </summary>
public sealed class WorkflowVersionMigrationItemTests
{
    // Use positive long values — guards enforce migrationId > 0 and instanceId > 0
    private const long MigrationId = 1L;
    private const long InstanceId = 10L;
    private const long OldStateId = 100L;
    private const long NewStateId = 200L;

    private static WorkflowVersionMigrationItem BuildItem() =>
        WorkflowVersionMigrationItem.Create(TestGuidGenerator.Create(), MigrationId, InstanceId, OldStateId, NewStateId);

    // --- Factory ---

    [Fact]
    public void Create_SetsCoreFields()
    {
        var item = BuildItem();

        item.MigrationId.Should().Be(MigrationId);
        item.InstanceId.Should().Be(InstanceId);
        item.OldStateId.Should().Be(OldStateId);
        item.NewStateId.Should().Be(NewStateId);
        // T3: MessageId (Guid AK) is set by IGuidGenerator; InternalId (long PK) is 0 in unit tests
        item.MessageId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_StartsAsPending()
    {
        var item = BuildItem();

        item.Status.Should().Be(MigrationItemStatus.Pending);
    }

    [Fact]
    public void Create_AttemptCountStartsAtZero()
    {
        var item = BuildItem();

        item.AttemptCount.Should().Be(0);
    }

    [Fact]
    public void Create_ZeroMigrationId_Throws()
    {
        // Guard: migrationId <= 0 is invalid
        var act = () => WorkflowVersionMigrationItem.Create(TestGuidGenerator.Create(), 0L, InstanceId, OldStateId, NewStateId);

        act.Should().Throw<ArgumentException>().WithMessage("*MigrationId*");
    }

    [Fact]
    public void Create_ZeroInstanceId_Throws()
    {
        // Guard: instanceId <= 0 is invalid
        var act = () => WorkflowVersionMigrationItem.Create(TestGuidGenerator.Create(), MigrationId, 0L, OldStateId, NewStateId);

        act.Should().Throw<ArgumentException>().WithMessage("*InstanceId*");
    }

    // --- Status transitions ---

    [Fact]
    public void MarkSucceeded_SetsStatusAndProcessedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var item = BuildItem();

        item.MarkSucceeded();

        item.Status.Should().Be(MigrationItemStatus.Succeeded);
        item.ProcessedAt.Should().NotBeNull();
        item.ProcessedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void MarkSucceeded_IncrementsAttemptCount()
    {
        var item = BuildItem();

        item.MarkSucceeded();

        item.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void MarkFailed_SetsStatusAndErrorMessage()
    {
        var item = BuildItem();

        item.MarkFailed("unmapped state");

        item.Status.Should().Be(MigrationItemStatus.Failed);
        item.ErrorMessage.Should().Be("unmapped state");
        item.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_IncrementsAttemptCount()
    {
        var item = BuildItem();

        item.MarkFailed("error");

        item.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void MarkSkipped_SetsStatusAndReason()
    {
        var item = BuildItem();

        item.MarkSkipped("concurrent transition");

        item.Status.Should().Be(MigrationItemStatus.Skipped);
        item.ErrorMessage.Should().Be("concurrent transition");
        item.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkSucceeded_FromAlreadySucceeded_IsIdempotent()
    {
        var item = BuildItem();
        item.MarkSucceeded();

        // Hangfire retry safety — calling again should not throw
        var act = () => item.MarkSucceeded();

        act.Should().NotThrow();
        item.Status.Should().Be(MigrationItemStatus.Succeeded);
    }
}
