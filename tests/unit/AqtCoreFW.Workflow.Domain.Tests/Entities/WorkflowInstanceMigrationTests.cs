using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Tests.Helpers;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// TDD tests for WorkflowInstance.RecordMigration().
/// All IDs are long (T1/T2 bigint PKs). IGuidGenerator required for MessageId generation.
/// Covers: field updates, history creation, domain event.
/// </summary>
public sealed class WorkflowInstanceMigrationTests
{
    private const long DefinitionId = 1L;
    private const long InitialStateId = 10L;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static WorkflowInstance CreateInstance() =>
        WorkflowInstance.Create(
            TestGuidGenerator.Create(),
            DefinitionId, InitialStateId, "Case",
            Guid.NewGuid(), TenantId, ActorId);

    [Fact]
    public void RecordMigration_SetsOriginalDefinitionId()
    {
        var instance = CreateInstance();
        const long sourceDefId = 1L;
        const long migrationId = 100L;
        const long newStateId = 20L;
        const long newDefId = 2L;

        instance.RecordMigration(TestGuidGenerator.Create(), sourceDefId, migrationId, newStateId, newDefId, ActorId);

        instance.OriginalDefinitionId.Should().Be(sourceDefId);
    }

    [Fact]
    public void RecordMigration_SetsLastMigrationId()
    {
        var instance = CreateInstance();
        const long migrationId = 100L;

        instance.RecordMigration(TestGuidGenerator.Create(), 1L, migrationId, 20L, 2L, ActorId);

        instance.LastMigrationId.Should().Be(migrationId);
    }

    [Fact]
    public void RecordMigration_UpdatesCurrentStateId()
    {
        var instance = CreateInstance();
        const long newStateId = 99L;

        instance.RecordMigration(TestGuidGenerator.Create(), 1L, 100L, newStateId, 2L, ActorId);

        instance.CurrentStateId.Should().Be(newStateId);
    }

    [Fact]
    public void RecordMigration_UpdatesDefinitionId()
    {
        var instance = CreateInstance();
        const long newDefId = 99L;

        instance.RecordMigration(TestGuidGenerator.Create(), 1L, 100L, 20L, newDefId, ActorId);

        instance.DefinitionId.Should().Be(newDefId);
    }

    [Fact]
    public void RecordMigration_AddsHistoryEntry()
    {
        var instance = CreateInstance();

        instance.RecordMigration(TestGuidGenerator.Create(), 1L, 100L, 20L, 2L, ActorId);

        instance.History.Should().ContainSingle(h => h.ActionName == "version-migration");
    }

    [Fact]
    public void RecordMigration_MultipleHops_OverwritesOriginalDefinitionId()
    {
        // Multi-hop: chain recorded via MigrationItems, OriginalDefinitionId is most recent hop
        var instance = CreateInstance();
        const long firstSourceId = 1L;
        const long secondSourceId = 2L;

        instance.RecordMigration(TestGuidGenerator.Create(), firstSourceId, 100L, 20L, 3L, ActorId);
        instance.RecordMigration(TestGuidGenerator.Create(), secondSourceId, 101L, 30L, 4L, ActorId);

        instance.OriginalDefinitionId.Should().Be(secondSourceId);
    }

    [Fact]
    public void RecordMigration_ZeroMigrationId_Throws()
    {
        var instance = CreateInstance();

        // migrationId <= 0 is invalid (T1 long PK must be positive after DB insert)
        var act = () => instance.RecordMigration(TestGuidGenerator.Create(), 1L, 0L, 20L, 2L, ActorId);

        act.Should().Throw<ArgumentException>().WithMessage("*migrationId*");
    }
}
