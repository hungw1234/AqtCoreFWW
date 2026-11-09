using AqtCoreFW.Workflow.Application.Queries.GetMigrationStatus;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetMigrationStatusQueryHandler.
/// Phase-4b B4c: WorkflowVersionMigration is T1 — Id is long (bigint PK, DB-assigned = 0 in unit tests).
/// GetByIdAsync takes long. Create(long, long, ...) uses positive longs.
/// </summary>
public sealed class GetMigrationStatusQueryHandlerTests
{
    // WorkflowVersionMigration.Create requires sourceDefinitionId > 0 and targetDefinitionId > 0
    private static WorkflowVersionMigration MakeMigration(Guid? tenantId = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        return WorkflowVersionMigration.Create(
            1L, 2L,
            "[{\"oldStateId\":1,\"newStateId\":2}]",
            tenant, Guid.NewGuid());
    }

    [Fact]
    public async Task Handle_ReturnsDto_WhenMigrationExists()
    {
        var repo = Substitute.For<IWorkflowVersionMigrationRepository>();
        var stateProvider = Substitute.For<IHangfireJobStateProvider>();
        var migration = MakeMigration();

        // migration.Id = 0 (long, DB-assigned, not set in unit tests)
        repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        stateProvider.GetJobState(null).Returns((string?)null);

        var handler = new GetMigrationStatusQueryHandler(repo, stateProvider);
        var result = await handler.Handle(
            new GetMigrationStatusQuery(migration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(migration.Id);
        result.Value.HangfireJobState.Should().BeNull(); // no job yet
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenMigrationMissing()
    {
        var repo = Substitute.For<IWorkflowVersionMigrationRepository>();
        var stateProvider = Substitute.For<IHangfireJobStateProvider>();
        const long missingId = 999L; // long PK, not Guid

        repo.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("not found"));

        var handler = new GetMigrationStatusQueryHandler(repo, stateProvider);
        var result = await handler.Handle(
            new GetMigrationStatusQuery(missingId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PopulatesHangfireJobState_WhenJobIdPresent()
    {
        var repo = Substitute.For<IWorkflowVersionMigrationRepository>();
        var stateProvider = Substitute.For<IHangfireJobStateProvider>();
        var migration = MakeMigration();

        migration.MarkApproved(Guid.NewGuid());
        migration.MarkStarted("job-42");

        repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        stateProvider.GetJobState("job-42").Returns("Processing");

        var handler = new GetMigrationStatusQueryHandler(repo, stateProvider);
        var result = await handler.Handle(
            new GetMigrationStatusQuery(migration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HangfireJobState.Should().Be("Processing");
    }
}
