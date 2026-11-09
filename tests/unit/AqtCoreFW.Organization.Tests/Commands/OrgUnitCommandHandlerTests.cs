using AqtCoreFW.Organization.Application.Commands;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.CommandHandlers;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Organization.Tests.Commands;

/// <summary>
/// Unit tests for OrgUnit CRUD command handlers using EF InMemory.
/// OrgUnit is T2: external addressing via PublicId (Guid); Commands accept Guid = PublicId.
/// TC-ORG-006: CreateOrgUnit conflict on duplicate code per tenant.
/// TC-ORG-007: CreateOrgUnit computes level from parent.
/// TC-ORG-008: CreateOrgUnit NotFound for missing parent.
/// TC-ORG-009: UpdateOrgUnit updates name and invalidates cache.
/// TC-ORG-010: UpdateOrgUnit NotFound for non-existent.
/// TC-ORG-011: DeleteOrgUnit blocks when active children exist.
/// TC-ORG-012: DeleteOrgUnit deactivates and sets successor.
/// TC-ORG-013: DeleteOrgUnit NotFound for wrong tenant.
/// </summary>
public sealed class OrgUnitCommandHandlerTests : IDisposable
{
    private static readonly Guid TenantId      = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly OrgDbContext _db;
    private readonly OrgUnitService _orgService;
    private readonly IGuidGenerator _gen;

    public OrgUnitCommandHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TenantId);
        tenantCtx.IsSystemAdmin.Returns(true);

        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new OrgDbContext(options, tenantCtx);

        var cache  = Substitute.For<AqtCoreFW.SharedKernel.Application.Caching.ICacheService>();
        var readDb = Substitute.For<AqtCoreFW.SharedKernel.Application.Data.IReadDbConnection>();
        _orgService = new OrgUnitService(_db, readDb, cache);

        // IGuidGenerator: each call returns a unique Guid
        _gen = Substitute.For<IGuidGenerator>();
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    // Helper: create and persist an OrgUnit, returning it (with DB-assigned long Id)
    private async Task<OrgUnit> SeedOrgUnitAsync(
        string name, string code, Guid tenantId, long? parentId = null, int level = 1)
    {
        var unit = OrgUnit.Create(_gen, name, name + " EN", code, tenantId, parentId, level);
        _db.OrgUnits.Add(unit);
        await _db.SaveChangesAsync();
        return unit;
    }

    // --- TC-ORG-006: Conflict on duplicate code per tenant ---

    [Fact]
    public async Task CreateOrgUnit_DuplicateCodeSameTenant_ReturnsConflictError()
    {
        await SeedOrgUnitAsync("Existing", "MOJ", TenantId);

        var handler = new CreateOrgUnitCommandHandler(_db, _orgService, _gen);
        var cmd = new CreateOrgUnitCommand(TenantId, "New Ministry", "New Ministry EN", "MOJ", null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("MOJ"));
    }

    [Fact]
    public async Task CreateOrgUnit_SameCodeDifferentTenant_Succeeds()
    {
        await SeedOrgUnitAsync("Existing", "MOJ", OtherTenantId);

        var handler = new CreateOrgUnitCommandHandler(_db, _orgService, _gen);
        var cmd = new CreateOrgUnitCommand(TenantId, "My Ministry", "My Ministry EN", "MOJ", null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- TC-ORG-007: Computes level from parent ---

    [Fact]
    public async Task CreateOrgUnit_WithParent_ComputesLevelAsParentLevelPlusOne()
    {
        var parent = await SeedOrgUnitAsync("Ministry", "MIN", TenantId, null, 1);
        // Command uses PublicId (Guid) to reference parent
        var handler = new CreateOrgUnitCommandHandler(_db, _orgService, _gen);
        var cmd = new CreateOrgUnitCommand(TenantId, "Department", "Department EN", "DEPT-01", parent.PublicId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Level.Should().Be(2);
        result.Value.ParentId.Should().Be(parent.PublicId);
    }

    [Fact]
    public async Task CreateOrgUnit_WithoutParent_DefaultsToLevelOne()
    {
        var handler = new CreateOrgUnitCommandHandler(_db, _orgService, _gen);
        var cmd = new CreateOrgUnitCommand(TenantId, "Root Ministry", "Root Ministry EN", "ROOT", null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Level.Should().Be(1);
    }

    // --- TC-ORG-008: NotFound for missing parent ---

    [Fact]
    public async Task CreateOrgUnit_MissingParentId_ReturnsNotFoundError()
    {
        var handler = new CreateOrgUnitCommandHandler(_db, _orgService, _gen);
        var missingParentPublicId = Guid.NewGuid();
        var cmd = new CreateOrgUnitCommand(TenantId, "Division", "Division EN", "DIV-01", missingParentPublicId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(missingParentPublicId.ToString()));
    }

    // --- TC-ORG-009: UpdateOrgUnit updates name ---

    [Fact]
    public async Task UpdateOrgUnit_ExistingUnit_UpdatesNameAndNameEn()
    {
        var unit = await SeedOrgUnitAsync("Old Name", "CODE-01", TenantId);

        var handler = new UpdateOrgUnitCommandHandler(_db, _orgService);
        // Use PublicId for update command
        var cmd = new UpdateOrgUnitCommand(unit.PublicId, TenantId, "New Name", "New EN");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("New Name");
        result.Value.NameEn.Should().Be("New EN");
    }

    [Fact]
    public async Task UpdateOrgUnit_ExistingUnit_InvalidatesCacheOnce()
    {
        var cache  = Substitute.For<AqtCoreFW.SharedKernel.Application.Caching.ICacheService>();
        var readDb = Substitute.For<AqtCoreFW.SharedKernel.Application.Data.IReadDbConnection>();
        var svc    = new OrgUnitService(_db, readDb, cache);

        var unit = await SeedOrgUnitAsync("Original", "ORIG", TenantId);

        var handler = new UpdateOrgUnitCommandHandler(_db, svc);
        var cmd = new UpdateOrgUnitCommand(unit.PublicId, TenantId, "Updated", "Updated EN");

        await handler.Handle(cmd, CancellationToken.None);

        await cache.Received(1).RemoveAsync(
            Arg.Is<string>(k => k.Contains(TenantId.ToString())),
            Arg.Any<CancellationToken>());
    }

    // --- TC-ORG-010: UpdateOrgUnit NotFound ---

    [Fact]
    public async Task UpdateOrgUnit_NonExistentId_ReturnsNotFoundError()
    {
        var handler = new UpdateOrgUnitCommandHandler(_db, _orgService);
        var cmd = new UpdateOrgUnitCommand(Guid.NewGuid(), TenantId, "Name", "Name EN");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    // --- TC-ORG-011: DeleteOrgUnit blocks when active children ---

    [Fact]
    public async Task DeleteOrgUnit_WithActiveChildren_ReturnsValidationError()
    {
        var parent = await SeedOrgUnitAsync("Parent", "PAR", TenantId, null, 1);
        // Child uses parent's long Id as parentId
        var child  = OrgUnit.Create(_gen, "Child", "Child EN", "CHI", TenantId, parent.Id, 2);
        _db.OrgUnits.Add(child);
        await _db.SaveChangesAsync();

        var handler = new DeleteOrgUnitCommandHandler(_db, _orgService);
        var cmd = new DeleteOrgUnitCommand(parent.PublicId, TenantId, null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("active children"));
    }

    // --- TC-ORG-012: DeleteOrgUnit deactivates and sets successor ---

    [Fact]
    public async Task DeleteOrgUnit_NoChildren_DeactivatesUnit()
    {
        var unit = await SeedOrgUnitAsync("Leaf Unit", "LEAF", TenantId);

        var handler = new DeleteOrgUnitCommandHandler(_db, _orgService);
        var cmd = new DeleteOrgUnitCommand(unit.PublicId, TenantId, null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _db.OrgUnits.IgnoreQueryFilters()
            .FirstAsync(x => x.PublicId == unit.PublicId);
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOrgUnit_WithSuccessorId_SetsSuccessorBeforeDeactivating()
    {
        var unit      = await SeedOrgUnitAsync("Old Unit", "OLD", TenantId);
        var successor = await SeedOrgUnitAsync("Successor", "NEW", TenantId);

        var handler = new DeleteOrgUnitCommandHandler(_db, _orgService);
        // SuccessorId in command = PublicId (Guid) of successor
        var cmd = new DeleteOrgUnitCommand(unit.PublicId, TenantId, successor.PublicId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _db.OrgUnits.IgnoreQueryFilters()
            .FirstAsync(x => x.PublicId == unit.PublicId);
        // SuccessorId on entity = long (internal Id of successor)
        updated.SuccessorId.Should().Be(successor.Id);
        updated.IsActive.Should().BeFalse();
    }

    // --- TC-ORG-013: DeleteOrgUnit NotFound for wrong tenant ---

    [Fact]
    public async Task DeleteOrgUnit_WrongTenant_ReturnsNotFoundError()
    {
        var unit = await SeedOrgUnitAsync("Other Tenant Unit", "OTHER", OtherTenantId);

        var handler = new DeleteOrgUnitCommandHandler(_db, _orgService);
        // Request with TenantId but unit belongs to OtherTenantId
        var cmd = new DeleteOrgUnitCommand(unit.PublicId, TenantId, null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }
}
