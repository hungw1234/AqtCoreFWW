using AqtCoreFW.Workflow.Application.Queries.GetMigrationsByDefinition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetMigrationsByDefinitionQueryHandler.
/// Verifies tenant filtering is delegated to repository and DTO mapping is correct.
/// </summary>
public sealed class GetMigrationsByDefinitionQueryHandlerTests
{
    private readonly IWorkflowVersionMigrationRepository _repo =
        Substitute.For<IWorkflowVersionMigrationRepository>();

    private readonly GetMigrationsByDefinitionQueryHandler _handler;

    public GetMigrationsByDefinitionQueryHandlerTests()
    {
        _handler = new GetMigrationsByDefinitionQueryHandler(_repo);
    }

    private static WorkflowVersionMigration MakeMigration(Guid tenantId) =>
        // T1 entity: sourceDefinitionId/targetDefinitionId are long (FK to T2 internal PK)
        WorkflowVersionMigration.Create(
            1L, 2L,
            "[{\"oldStateId\":1,\"newStateId\":2}]",
            tenantId, Guid.NewGuid());

    [Fact]
    public async Task Handle_ReturnsMigrations_ForTenant()
    {
        var tenantId = Guid.NewGuid();
        var migration = MakeMigration(tenantId);

        _repo.ListByDefinitionKeyAsync("key", tenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowVersionMigration>)[migration]);

        var result = await _handler.Handle(
            new GetMigrationsByDefinitionQuery("key", tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(m => m.Id == migration.Id);
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoMigrationsExist()
    {
        var tenantId = Guid.NewGuid();

        _repo.ListByDefinitionKeyAsync("no-key", tenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowVersionMigration>)[]);

        var result = await _handler.Handle(
            new GetMigrationsByDefinitionQuery("no-key", tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DoesNotMixTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var migA = MakeMigration(tenantA);

        // tenantA has 1 migration, tenantB has none (repository isolates them)
        _repo.ListByDefinitionKeyAsync("key", tenantA, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowVersionMigration>)[migA]);
        _repo.ListByDefinitionKeyAsync("key", tenantB, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowVersionMigration>)[]);

        var resultA = await _handler.Handle(
            new GetMigrationsByDefinitionQuery("key", tenantA), CancellationToken.None);
        var resultB = await _handler.Handle(
            new GetMigrationsByDefinitionQuery("key", tenantB), CancellationToken.None);

        resultA.Value.Should().HaveCount(1);
        resultB.Value.Should().BeEmpty();
    }
}
