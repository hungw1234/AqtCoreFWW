using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Reporting.Application.Tests.Domain;

/// <summary>
/// Pure domain tests for ReportExecution entity — no mocks, no infrastructure.
/// Verifies all state-transition methods set the correct properties.
/// T2 entity: PublicId (Guid) set in factory via IGuidGenerator; Id (long) assigned by EF on SaveChanges.
/// </summary>
public sealed class ReportExecutionEntityTests
{
    // T1 FK: ReportDefinitionId is now long (internal PK of ReportDefinition T2 entity)
    private const long DefinitionId = 42L;
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly IGuidGenerator GuidGen = Substitute.For<IGuidGenerator>();

    static ReportExecutionEntityTests()
    {
        // Stub IGuidGenerator to return deterministic Guid for tests
        GuidGen.NewIdMonotonic().Returns(Guid.Parse("11111111-1111-7111-8111-111111111111"));
    }

    [Fact]
    public void Create_SetsQueuedStatus()
    {
        // Act
        var execution = ReportExecution.Create(
            gen: GuidGen,
            reportDefinitionId: DefinitionId,
            parameters: "{}",
            requestedBy: UserId);

        // Assert
        // T2: PublicId is Guid set by IGuidGenerator; Id is long (0 before EF SaveChanges)
        execution.PublicId.Should().NotBeEmpty();
        execution.Id.Should().Be(0L); // assigned by EF on SaveChanges
        execution.ReportDefinitionId.Should().Be(DefinitionId);
        execution.RequestedBy.Should().Be(UserId);
        execution.Status.Should().Be(ExecutionStatus.Queued);
        execution.StartedAt.Should().BeNull();
        execution.CompletedAt.Should().BeNull();
        execution.ResultFilePath.Should().BeNull();
        execution.ErrorMessage.Should().BeNull();
        execution.RequestedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkRunning_SetsRunningAndStartedAt()
    {
        // Arrange
        var execution = ReportExecution.Create(GuidGen, DefinitionId, "{}", UserId);

        // Act
        execution.MarkRunning();

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Running);
        execution.StartedAt.Should().NotBeNull();
        execution.StartedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
        execution.CompletedAt.Should().BeNull(); // not yet completed
    }

    [Fact]
    public void MarkDone_SetsDoneAndResultFilePath()
    {
        // Arrange
        var execution = ReportExecution.Create(GuidGen, DefinitionId, "{}", UserId);
        execution.MarkRunning();
        const string filePath = "reports/2025/case-report.xlsx";

        // Act
        execution.MarkDone(filePath);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Done);
        execution.ResultFilePath.Should().Be(filePath);
        execution.CompletedAt.Should().NotBeNull();
        execution.CompletedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
        execution.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_SetsFailedAndErrorMessage()
    {
        // Arrange
        var execution = ReportExecution.Create(GuidGen, DefinitionId, "{}", UserId);
        execution.MarkRunning();
        const string errorMessage = "SQL timeout after 30 seconds.";

        // Act
        execution.MarkFailed(errorMessage);

        // Assert
        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Be(errorMessage);
        execution.CompletedAt.Should().NotBeNull();
        execution.CompletedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
        execution.ResultFilePath.Should().BeNull();
    }
}
