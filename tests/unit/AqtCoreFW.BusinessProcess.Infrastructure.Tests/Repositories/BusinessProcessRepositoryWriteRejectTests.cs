using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Tests.Repositories;

/// <summary>
/// Pattern-A R13 guard enforcement at repo write path. Cross-tenant Add/Update MUST throw
/// GOV_BP_002 before any DB interaction (defense-in-depth above DB-level RLS policy).
///
/// Positive (matching-tenant) save path is exercised indirectly by build-time entity
/// configuration and is not retested here — InMemory provider does not honour the
/// IsRowVersion() concurrency token, so a positive save here would test the harness,
/// not the repo guard.
/// </summary>
public sealed class BusinessProcessRepositoryWriteRejectTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static (BusinessProcessRepository repo, BusinessProcessDbContext db) NewRepo()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(CurrentTenantId);
        tenantContext.IsSystemAdmin.Returns(false);

        var options = new DbContextOptionsBuilder<BusinessProcessDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new BusinessProcessDbContext(options, tenantContext);
        var repo = new BusinessProcessRepository(db, tenantContext, NullLogger<BusinessProcessRepository>.Instance);
        return (repo, db);
    }

    private static IGuidGenerator Gen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static BusinessProcessDefinition NewBp(Guid tenantId) =>
        BusinessProcessDefinition.Create(Gen(), tenantId, "bp", "Name", 42L, ActorId);

    [Fact]
    public async Task AddAsync_CrossTenant_ThrowsGovBp002()
    {
        var (repo, db) = NewRepo();
        var foreignBp = NewBp(OtherTenantId);

        var act = async () => await repo.AddAsync(foreignBp);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*GOV_BP_002*");

        db.BusinessProcessDefinitions.Local.Should().BeEmpty(
            "guard MUST fire before EF tracks the entity");
    }

    [Fact]
    public async Task UpdateAsync_CrossTenant_ThrowsGovBp002()
    {
        var (repo, _) = NewRepo();
        var foreignBp = NewBp(OtherTenantId);

        var act = async () => await repo.UpdateAsync(foreignBp);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*GOV_BP_002*");
    }
}
