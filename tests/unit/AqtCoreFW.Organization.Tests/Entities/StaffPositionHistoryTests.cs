using AqtCoreFW.Organization.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Organization.Tests.Entities;

/// <summary>
/// Unit tests for StaffPositionHistory entity (T1: long Id, long OrgUnitId).
/// Covers Create factory defaults (TC-ORG-002) and Close method (TC-ORG-003).
/// </summary>
public sealed class StaffPositionHistoryTests
{
    private static readonly Guid UserId   = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const long OrgUnitId          = 42L; // long FK to OrgUnit.Id (T1 entity)
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // --- TC-ORG-002: Create sets defaults ---

    [Fact]
    public void Create_SetsAllRequiredFields()
    {
        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Senior Officer", TenantId);

        history.UserId.Should().Be(UserId);
        history.OrgUnitId.Should().Be(OrgUnitId);
        history.PositionTitle.Should().Be("Senior Officer");
        history.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_WithNoStartDate_DefaultsStartDateToApproximatelyUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Director", TenantId);

        history.StartDate.Should().BeAfter(before);
        history.StartDate.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Create_WithExplicitStartDate_UsesProvidedDate()
    {
        var explicitStart = new DateTimeOffset(2025, 1, 15, 8, 0, 0, TimeSpan.Zero);

        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Officer", TenantId, explicitStart);

        history.StartDate.Should().Be(explicitStart);
    }

    [Fact]
    public void Create_EndDate_IsNullByDefault()
    {
        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Officer", TenantId);

        history.EndDate.Should().BeNull();
    }

    [Fact]
    public void Create_OrgUnitId_IsSet()
    {
        // T1 entity: Id (long) is DB-assigned (0 before SaveChanges); OrgUnitId is long FK.
        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Officer", TenantId);

        history.OrgUnitId.Should().Be(OrgUnitId);
    }

    // --- TC-ORG-003: Close sets EndDate ---

    [Fact]
    public void Close_SetsEndDate()
    {
        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Officer", TenantId);
        var endDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        history.Close(endDate);

        history.EndDate.Should().Be(endDate);
    }

    [Fact]
    public void Close_SetsUpdatedAt()
    {
        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Officer", TenantId);
        var before = DateTime.UtcNow.AddSeconds(-1); // UpdatedAt is DateTime (Entity.cs)

        history.Close(DateTimeOffset.UtcNow);

        history.UpdatedAt.Should().NotBeNull();
        history.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void Close_AfterOpen_EndDateIsAfterStartDate()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-30);
        var history = StaffPositionHistory.Create(UserId, OrgUnitId, "Officer", TenantId, start);
        var end = DateTimeOffset.UtcNow;

        history.Close(end);

        history.EndDate.Should().BeAfter(history.StartDate);
    }
}
