using AqtCoreFW.Organization.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Organization.Tests.Entities;

/// <summary>
/// Unit tests for UserOrgUnitAssignment entity (T1: long Id, long OrgUnitId).
/// TC-ORG-004: Create IsActive=true by default.
/// TC-ORG-005: Close deactivates and sets ValidTo.
/// </summary>
public sealed class UserOrgUnitAssignmentTests
{
    private static readonly Guid UserId    = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const long OrgUnitId           = 42L; // long FK to OrgUnit.Id (T1 entity)
    private static readonly Guid TenantId  = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // --- TC-ORG-004: Create IsActive=true by default ---

    [Fact]
    public void Create_SetsAllRequiredFields()
    {
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Member", TenantId);

        assignment.UserId.Should().Be(UserId);
        assignment.OrgUnitId.Should().Be(OrgUnitId);
        assignment.RoleInOrg.Should().Be("Member");
        assignment.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_IsActiveByDefault()
    {
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Lead", TenantId);

        assignment.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_ValidTo_IsNullByDefault()
    {
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Deputy", TenantId);

        assignment.ValidTo.Should().BeNull();
    }

    [Fact]
    public void Create_WithNoValidFrom_DefaultsToApproximatelyUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Member", TenantId);

        assignment.ValidFrom.Should().BeAfter(before);
        assignment.ValidFrom.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Create_WithExplicitValidFrom_UsesProvidedDate()
    {
        var validFrom = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Lead", TenantId, validFrom);

        assignment.ValidFrom.Should().Be(validFrom);
    }

    [Fact]
    public void Create_OrgUnitId_IsSet()
    {
        // T1 entity: Id (long) is DB-assigned (0 before SaveChanges).
        // Verify OrgUnitId (long FK) is correctly set by factory.
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Member", TenantId);

        assignment.OrgUnitId.Should().Be(OrgUnitId);
    }

    // --- TC-ORG-005: Close deactivates ---

    [Fact]
    public void Close_SetsIsActiveFalse()
    {
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Member", TenantId);

        assignment.Close();

        assignment.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Close_SetsValidToApproximatelyUtcNow_WhenNoArgumentProvided()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Member", TenantId);

        assignment.Close();

        assignment.ValidTo.Should().NotBeNull();
        assignment.ValidTo.Should().BeAfter(before);
        assignment.ValidTo.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Close_WithExplicitDate_SetsValidToProvidedDate()
    {
        var closedAt = new DateTimeOffset(2026, 1, 31, 17, 0, 0, TimeSpan.Zero);
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Lead", TenantId);

        assignment.Close(closedAt);

        assignment.ValidTo.Should().Be(closedAt);
    }

    [Fact]
    public void Close_SetsUpdatedAt()
    {
        var assignment = UserOrgUnitAssignment.Create(UserId, OrgUnitId, "Member", TenantId);
        var before = DateTime.UtcNow.AddSeconds(-1); // UpdatedAt is DateTime (Entity.cs)

        assignment.Close();

        assignment.UpdatedAt.Should().NotBeNull();
        assignment.UpdatedAt.Should().BeAfter(before);
    }
}
