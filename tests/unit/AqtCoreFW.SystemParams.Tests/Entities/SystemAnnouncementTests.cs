using AqtCoreFW.SystemParams.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.SystemParams.Tests.Entities;

/// <summary>
/// TC-SP-D001: SystemAnnouncement Create and lifecycle (Update, Deactivate).
/// </summary>
public sealed class SystemAnnouncementTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddDays(7);

        var ann = SystemAnnouncement.Create(
            "Maintenance", "System maintenance tonight",
            AnnouncementSeverity.Warning, start, end, targetRoles: "[\"Admin\"]");

        ann.Title.Should().Be("Maintenance");
        ann.Content.Should().Be("System maintenance tonight");
        ann.Severity.Should().Be(AnnouncementSeverity.Warning);
        ann.StartAt.Should().Be(start);
        ann.EndAt.Should().Be(end);
        ann.TargetRoles.Should().Be("[\"Admin\"]");
        ann.IsActive.Should().BeTrue();
        // Id is long (T1 bigint PK, DB-assigned post-SaveChanges); pre-save value is 0 by EF convention.
        ann.Id.Should().Be(0);
    }

    [Fact]
    public void Create_WithoutTargetRoles_TargetRolesIsNull()
    {
        var ann = SystemAnnouncement.Create("Title", "Content", AnnouncementSeverity.Info);

        ann.TargetRoles.Should().BeNull();
    }

    [Fact]
    public void Create_DefaultSeverityInfo_IsActive()
    {
        var ann = SystemAnnouncement.Create("Title", "Content", AnnouncementSeverity.Info);

        ann.Severity.Should().Be(AnnouncementSeverity.Info);
        ann.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Update_ChangesAllMutableFields()
    {
        var ann = SystemAnnouncement.Create("Old", "Old content", AnnouncementSeverity.Info);
        var newStart = DateTimeOffset.UtcNow.AddDays(1);
        var newEnd = DateTimeOffset.UtcNow.AddDays(10);

        ann.Update("New Title", "New content", AnnouncementSeverity.Critical,
            newStart, newEnd, isActive: true);

        ann.Title.Should().Be("New Title");
        ann.Content.Should().Be("New content");
        ann.Severity.Should().Be(AnnouncementSeverity.Critical);
        ann.StartAt.Should().Be(newStart);
        ann.EndAt.Should().Be(newEnd);
        ann.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Update_SetsUpdatedAt()
    {
        var ann = SystemAnnouncement.Create("Title", "Content", AnnouncementSeverity.Info);
        // UpdatedAt is DateTime (UTC, datetime2(3)) per Phase-4a Entity refactor.
        var before = DateTime.UtcNow.AddSeconds(-1);

        ann.Update("New", "New", AnnouncementSeverity.Info, null, null, true);

        ann.UpdatedAt.Should().NotBeNull();
        ann.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var ann = SystemAnnouncement.Create("Title", "Content", AnnouncementSeverity.Warning);

        ann.Deactivate();

        ann.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_SetsUpdatedAt()
    {
        var ann = SystemAnnouncement.Create("Title", "Content", AnnouncementSeverity.Warning);
        // UpdatedAt is DateTime (UTC, datetime2(3)) per Phase-4a Entity refactor.
        var before = DateTime.UtcNow.AddSeconds(-1);

        ann.Deactivate();

        ann.UpdatedAt.Should().NotBeNull();
        ann.UpdatedAt.Should().BeAfter(before);
    }
}
