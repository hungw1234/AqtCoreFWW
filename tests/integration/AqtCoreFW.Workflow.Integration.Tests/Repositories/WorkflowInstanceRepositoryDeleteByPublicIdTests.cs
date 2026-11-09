using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Repositories;

/// <summary>
/// Regression guard for WorkflowInstanceRepository.DeleteAsync(Guid) — the override that resolves the
/// T2 entity by its Guid PublicId (alternate key) before removing it. The generic GenericRepository
/// base issues EF FindAsync, which keys on the long PRIMARY KEY, so a Guid lookup throws the
/// "Guid ... does not match long" key-type mismatch. No production path hard-deletes a WorkflowInstance
/// today, so this is a defensive guard against the latent throw a future delete path would otherwise hit.
///
/// Real EF + Testcontainers SQL Server through the real repository override. Non-vacuous: pre-override
/// the DeleteAsync(Guid) call throws (no row touched); here it must succeed and soft-delete the row.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstanceRepositoryDeleteByPublicIdTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public WorkflowInstanceRepositoryDeleteByPublicIdTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeleteAsync_ResolvesInstanceByPublicId_SoftDeletesTheRow()
    {
        // Arrange: seed a definition (for the FK) then a WorkflowInstance; capture its Guid PublicId + long Id.
        var def = await InlineWorkflowDefinitionFactory.CreateAsync(_fixture.Services, TenantId, ActorId);

        Guid publicId;
        long internalId;
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var instance = WorkflowInstance.Create(
                gen: gen,
                definitionId: def.DefinitionId,
                initialStateId: def.DraftStateId,
                entityType: "IntegrationTest",
                entityId: Guid.NewGuid(),
                tenantId: TenantId,
                createdBy: ActorId);

            db.Set<WorkflowInstance>().Add(instance);
            await db.SaveChangesAsync();
            publicId = instance.PublicId;
            internalId = instance.Id;
        }

        // Act: delete addressed by the Guid PublicId (the override must resolve PublicId → long, not FindAsync the PK).
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
            await repo.DeleteAsync(publicId); // pre-override this throws the Guid-vs-long key mismatch
        }

        // Assert: the row was soft-deleted (IgnoreQueryFilters to see past the IsDeleted filter).
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var row = await db.Set<WorkflowInstance>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == internalId);

            row.Should().NotBeNull("the seeded instance row must still exist (soft-delete, not hard-delete)");
            row!.IsDeleted.Should().BeTrue("DeleteAsync(publicId) must resolve by PublicId and soft-delete the row");
        }
    }
}
