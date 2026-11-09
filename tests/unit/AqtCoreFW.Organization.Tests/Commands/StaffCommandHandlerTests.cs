using AqtCoreFW.Organization.Application.Commands;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.CommandHandlers;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Organization.Tests.Commands;

/// <summary>
/// Unit tests for staff assignment command handlers using EF InMemory.
/// OrgUnit is T2: commands pass OrgUnitId as PublicId (Guid).
/// T1 entities (UserOrgUnitAssignment, StaffPositionHistory): Id and OrgUnitId are long.
/// TC-ORG-017: AssignStaff creates assignment + position history.
/// TC-ORG-018: TransferStaff closes old assignment/history, creates new ones.
/// </summary>
public sealed class StaffCommandHandlerTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly OrgDbContext _db;
    private readonly IGuidGenerator _gen;

    public StaffCommandHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TenantId);
        tenantCtx.IsSystemAdmin.Returns(true);

        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new OrgDbContext(options, tenantCtx);

        _gen = Substitute.For<IGuidGenerator>();
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    // Helper: seed an active org unit, return its PublicId (Guid) for use in commands.
    // The entity.Id (long) is DB-assigned; handler resolves PublicId→long internally.
    private async Task<Guid> SeedActiveOrgUnitAsync(string code = "UNIT-01")
    {
        var unit = OrgUnit.Create(_gen, "Unit", "Unit EN", code, TenantId);
        _db.OrgUnits.Add(unit);
        await _db.SaveChangesAsync();
        return unit.PublicId; // PublicId = Guid, used as external Id in commands
    }

    // --- TC-ORG-017: AssignStaff creates assignment + position history ---

    [Fact]
    public async Task AssignStaff_ValidOrgUnit_CreatesActiveAssignment()
    {
        var orgUnitPublicId = await SeedActiveOrgUnitAsync();
        var handler = new AssignStaffCommandHandler(_db);
        var cmd = new AssignStaffCommand(TenantId, UserId, orgUnitPublicId, "Member", "Officer II");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(UserId);
        result.Value.RoleInOrg.Should().Be("Member");
        result.Value.IsActive.Should().BeTrue();
        // OrgUnitId in DTO is long (T1 entity FK)
        result.Value.OrgUnitId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AssignStaff_ValidOrgUnit_PersistsAssignmentToDb()
    {
        var orgUnitPublicId = await SeedActiveOrgUnitAsync();
        var handler = new AssignStaffCommandHandler(_db);
        var cmd = new AssignStaffCommand(TenantId, UserId, orgUnitPublicId, "Lead", "Division Lead");

        await handler.Handle(cmd, CancellationToken.None);

        var assignment = await _db.Assignments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.UserId == UserId && a.TenantId == TenantId);
        assignment.Should().NotBeNull();
        assignment!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AssignStaff_ValidOrgUnit_PersistsPositionHistoryWithOpenEndDate()
    {
        var orgUnitPublicId = await SeedActiveOrgUnitAsync();
        var handler = new AssignStaffCommandHandler(_db);
        var cmd = new AssignStaffCommand(TenantId, UserId, orgUnitPublicId, "Member", "Senior Inspector");

        await handler.Handle(cmd, CancellationToken.None);

        var history = await _db.PositionHistories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.UserId == UserId && h.TenantId == TenantId);
        history.Should().NotBeNull();
        history!.PositionTitle.Should().Be("Senior Inspector");
        history.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task AssignStaff_InactiveOrgUnit_ReturnsNotFoundError()
    {
        // Seed an inactive unit by deactivating after creation
        var unit = OrgUnit.Create(_gen, "Inactive", "Inactive EN", "INACT", TenantId);
        _db.OrgUnits.Add(unit);
        await _db.SaveChangesAsync();
        unit.Deactivate();
        await _db.SaveChangesAsync();

        var handler = new AssignStaffCommandHandler(_db);
        var cmd = new AssignStaffCommand(TenantId, UserId, unit.PublicId, "Member", "Officer");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task AssignStaff_NonExistentOrgUnit_ReturnsNotFoundError()
    {
        var handler = new AssignStaffCommandHandler(_db);
        var cmd = new AssignStaffCommand(TenantId, UserId, Guid.NewGuid(), "Member", "Officer");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- TC-ORG-018: TransferStaff closes old, creates new ---

    [Fact]
    public async Task TransferStaff_ClosesExistingAssignmentAndCreatesNew()
    {
        var fromUnitPublicId = await SeedActiveOrgUnitAsync("FROM");
        var toUnitPublicId   = await SeedActiveOrgUnitAsync("TO");

        var assignHandler = new AssignStaffCommandHandler(_db);
        await assignHandler.Handle(
            new AssignStaffCommand(TenantId, UserId, fromUnitPublicId, "Member", "Officer"),
            CancellationToken.None);

        var transferHandler = new TransferStaffCommandHandler(_db);
        var cmd = new TransferStaffCommand(TenantId, UserId, toUnitPublicId, "Lead", "Team Lead");

        var result = await transferHandler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RoleInOrg.Should().Be("Lead");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TransferStaff_ClosesOldAssignment()
    {
        var fromUnitPublicId = await SeedActiveOrgUnitAsync("FROM2");
        var toUnitPublicId   = await SeedActiveOrgUnitAsync("TO2");

        // Get the long Id for fromUnit to query by OrgUnitId (T1 FK = long)
        var fromUnit = await _db.OrgUnits.IgnoreQueryFilters()
            .FirstAsync(o => o.PublicId == fromUnitPublicId);

        var assignHandler = new AssignStaffCommandHandler(_db);
        await assignHandler.Handle(
            new AssignStaffCommand(TenantId, UserId, fromUnitPublicId, "Member", "Officer"),
            CancellationToken.None);

        var transferHandler = new TransferStaffCommandHandler(_db);
        await transferHandler.Handle(
            new TransferStaffCommand(TenantId, UserId, toUnitPublicId, "Lead", "Team Lead"),
            CancellationToken.None);

        // Old assignment: OrgUnitId = fromUnit.Id (long)
        var oldAssignment = await _db.Assignments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.OrgUnitId == fromUnit.Id && a.UserId == UserId);
        oldAssignment.Should().NotBeNull();
        oldAssignment!.IsActive.Should().BeFalse();
        oldAssignment.ValidTo.Should().NotBeNull();
    }

    [Fact]
    public async Task TransferStaff_ClosesOpenPositionHistory()
    {
        var fromUnitPublicId = await SeedActiveOrgUnitAsync("FROM3");
        var toUnitPublicId   = await SeedActiveOrgUnitAsync("TO3");

        var fromUnit = await _db.OrgUnits.IgnoreQueryFilters()
            .FirstAsync(o => o.PublicId == fromUnitPublicId);

        var assignHandler = new AssignStaffCommandHandler(_db);
        await assignHandler.Handle(
            new AssignStaffCommand(TenantId, UserId, fromUnitPublicId, "Member", "Inspector"),
            CancellationToken.None);

        var transferHandler = new TransferStaffCommandHandler(_db);
        await transferHandler.Handle(
            new TransferStaffCommand(TenantId, UserId, toUnitPublicId, "Lead", "Senior Inspector"),
            CancellationToken.None);

        var oldHistory = await _db.PositionHistories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.OrgUnitId == fromUnit.Id && h.UserId == UserId);
        oldHistory.Should().NotBeNull();
        oldHistory!.EndDate.Should().NotBeNull();
    }

    [Fact]
    public async Task TransferStaff_CreatesNewPositionHistory()
    {
        var fromUnitPublicId = await SeedActiveOrgUnitAsync("FROM4");
        var toUnitPublicId   = await SeedActiveOrgUnitAsync("TO4");

        var toUnit = await _db.OrgUnits.IgnoreQueryFilters()
            .FirstAsync(o => o.PublicId == toUnitPublicId);

        var assignHandler = new AssignStaffCommandHandler(_db);
        await assignHandler.Handle(
            new AssignStaffCommand(TenantId, UserId, fromUnitPublicId, "Member", "Officer"),
            CancellationToken.None);

        var transferHandler = new TransferStaffCommandHandler(_db);
        await transferHandler.Handle(
            new TransferStaffCommand(TenantId, UserId, toUnitPublicId, "Lead", "Division Head"),
            CancellationToken.None);

        var newHistory = await _db.PositionHistories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.OrgUnitId == toUnit.Id && h.UserId == UserId);
        newHistory.Should().NotBeNull();
        newHistory!.PositionTitle.Should().Be("Division Head");
        newHistory.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task TransferStaff_TargetOrgUnitNotFound_ReturnsNotFoundError()
    {
        var transferHandler = new TransferStaffCommandHandler(_db);
        var cmd = new TransferStaffCommand(TenantId, UserId, Guid.NewGuid(), "Lead", "Head");

        var result = await transferHandler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }
}
