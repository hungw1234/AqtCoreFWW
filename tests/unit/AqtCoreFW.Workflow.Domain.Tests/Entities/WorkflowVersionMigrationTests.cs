using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// TDD tests for WorkflowVersionMigration aggregate.
/// T1 entity: Id=long (bigint PK — 0 in unit tests without DB).
/// SourceDefinitionId/TargetDefinitionId: long (T2 WorkflowDefinition.Id).
/// Covers: factory, hash computation, status transitions, cancel guard.
/// </summary>
public sealed class WorkflowVersionMigrationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    // long IDs (T2 WorkflowDefinition.Id bigint PKs — positive values required by guard)
    private const long SourceDefId = 1L;
    private const long TargetDefId = 2L;
    private static readonly Guid CreatedBy = Guid.NewGuid();
    private const string ValidMappingsJson = """[{"oldStateId":"aaa","newStateId":"bbb"}]""";

    private static WorkflowVersionMigration BuildMigration(string? json = null) =>
        WorkflowVersionMigration.Create(
            SourceDefId, TargetDefId,
            json ?? ValidMappingsJson,
            TenantId, CreatedBy);

    // --- Factory ---

    [Fact]
    public void Create_SetsCoreFields()
    {
        var m = BuildMigration();

        m.SourceDefinitionId.Should().Be(SourceDefId);
        m.TargetDefinitionId.Should().Be(TargetDefId);
        m.StateMappingsJson.Should().Be(ValidMappingsJson);
        m.TenantId.Should().Be(TenantId);
        m.CreatedBy.Should().Be(CreatedBy);
        // Id=long, starts at 0 in unit tests (DB-assigned); just verify it's accessible
        m.Id.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Create_StartsAtPendingApproval()
    {
        var m = BuildMigration();

        m.Status.Should().Be(MigrationStatus.PendingApproval);
    }

    [Fact]
    public void Create_ComputesAndStoresMappingHash()
    {
        var m = BuildMigration();

        m.MappingHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_ZeroSourceDefId_Throws()
    {
        // Guard: sourceDefinitionId must be positive (T1/T2 long PK)
        var act = () => WorkflowVersionMigration.Create(
            0L, TargetDefId, ValidMappingsJson, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*SourceDefinitionId*");
    }

    [Fact]
    public void Create_ZeroTargetDefId_Throws()
    {
        var act = () => WorkflowVersionMigration.Create(
            SourceDefId, 0L, ValidMappingsJson, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*TargetDefinitionId*");
    }

    [Fact]
    public void Create_SameSourceAndTarget_Throws()
    {
        const long sameId = 5L;
        var act = () => WorkflowVersionMigration.Create(
            sameId, sameId, ValidMappingsJson, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullMappingsJson_Throws()
    {
        var act = () => WorkflowVersionMigration.Create(
            SourceDefId, TargetDefId, null!, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*StateMappingsJson*");
    }

    // --- Hash computation ---

    [Fact]
    public void ComputeMappingHash_SameJson_ReturnsSameHash()
    {
        var m1 = BuildMigration(ValidMappingsJson);
        var m2 = BuildMigration(ValidMappingsJson);

        m1.MappingHash.Should().Be(m2.MappingHash);
    }

    [Fact]
    public void ComputeMappingHash_DifferentJson_ReturnsDifferentHash()
    {
        var m1 = BuildMigration("""[{"oldStateId":"aaa","newStateId":"bbb"}]""");
        var m2 = BuildMigration("""[{"oldStateId":"xxx","newStateId":"yyy"}]""");

        m1.MappingHash.Should().NotBe(m2.MappingHash);
    }

    [Fact]
    public void VerifyMappingHash_MatchingJson_DoesNotThrow()
    {
        var m = BuildMigration();

        var act = () => m.VerifyMappingHash();

        act.Should().NotThrow();
    }

    // --- Status transitions ---

    [Fact]
    public void MarkStarted_FromApproved_SetsInProgress()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());

        m.MarkStarted("job-123");

        m.Status.Should().Be(MigrationStatus.InProgress);
        m.HangfireJobId.Should().Be("job-123");
        m.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkStarted_FromPendingApproval_Throws()
    {
        var m = BuildMigration();

        var act = () => m.MarkStarted("job-1");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkCompleted_FromInProgress_SetsCompleted()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());
        m.MarkStarted("job-1");

        m.MarkCompleted();

        m.Status.Should().Be(MigrationStatus.Completed);
        m.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_FromInProgress_SetsFailed()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());
        m.MarkStarted("job-1");

        m.MarkFailed();

        m.Status.Should().Be(MigrationStatus.Failed);
        m.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkCancelled_FromInProgress_SetsCancelled()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());
        m.MarkStarted("job-1");

        m.MarkCancelled();

        m.Status.Should().Be(MigrationStatus.Cancelled);
        m.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkApproved_StoresApproverAndApprovedAt()
    {
        var approverId = Guid.NewGuid();
        var m = BuildMigration();

        m.MarkApproved(approverId);

        m.Status.Should().Be(MigrationStatus.Approved);
        m.ApprovedBy.Should().Be(approverId);
        m.ApprovedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkApproved_FromNonPendingApproval_Throws()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());

        var act = () => m.MarkApproved(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    // --- RequestCancel ---

    [Fact]
    public void RequestCancel_FromPendingApproval_SetsCancelRequested()
    {
        var m = BuildMigration();

        m.RequestCancel();

        m.CancelRequested.Should().BeTrue();
    }

    [Fact]
    public void RequestCancel_FromApproved_SetsCancelRequested()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());

        m.RequestCancel();

        m.CancelRequested.Should().BeTrue();
    }

    [Fact]
    public void RequestCancel_FromInProgress_SetsCancelRequested()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());
        m.MarkStarted("job-1");

        m.RequestCancel();

        m.CancelRequested.Should().BeTrue();
    }

    [Fact]
    public void RequestCancel_FromCompleted_Throws()
    {
        var m = BuildMigration();
        m.MarkApproved(Guid.NewGuid());
        m.MarkStarted("job-1");
        m.MarkCompleted();

        var act = () => m.RequestCancel();

        act.Should().Throw<InvalidOperationException>();
    }

    // --- IncrementCounts ---

    [Fact]
    public void IncrementMigratedCount_IncreasesCount()
    {
        var m = BuildMigration();

        m.IncrementMigratedCount();
        m.IncrementMigratedCount();

        m.MigratedCount.Should().Be(2);
    }

    [Fact]
    public void IncrementFailedCount_IncreasesCount()
    {
        var m = BuildMigration();

        m.IncrementFailedCount();

        m.FailedCount.Should().Be(1);
    }

    [Fact]
    public void IncrementSkippedCount_IncreasesCount()
    {
        var m = BuildMigration();

        m.IncrementSkippedCount();

        m.SkippedCount.Should().Be(1);
    }

    [Fact]
    public void SetTotalInstances_UpdatesTotal()
    {
        var m = BuildMigration();

        m.SetTotalInstances(42);

        m.TotalInstances.Should().Be(42);
    }
}
