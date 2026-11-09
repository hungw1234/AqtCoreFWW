using AqtCoreFW.Reporting.Domain.Entities;

namespace AqtCoreFW.Reporting.Domain.Tests.Entities;

/// <summary>
/// Unit tests for DashboardLayout entity.
/// Validates Create factory, SetAsDefault, UpdateLayout, and Rename behaviour.
/// </summary>
public sealed class DashboardLayoutEntityTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    // --- Create ---

    [Fact]
    public void Create_WithValidArgs_SetsAllProperties()
    {
        var layout = DashboardLayout.Create("My Dashboard", OwnerId, TenantId);

        // T1 entity: Id is long assigned by EF on SaveChanges (0 before persisted)
        layout.Id.Should().Be(0L);
        layout.Name.Should().Be("My Dashboard");
        layout.OwnerId.Should().Be(OwnerId);
        layout.TenantId.Should().Be(TenantId);
        layout.IsDefault.Should().BeFalse();
        layout.WidgetPositions.Should().Be("[]");
        layout.CreatedBy.Should().Be(OwnerId);
        layout.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_WithIsDefault_SetsDefaultTrue()
    {
        var layout = DashboardLayout.Create("Default Dashboard", OwnerId, TenantId, isDefault: true);

        layout.IsDefault.Should().BeTrue();
    }

    // --- SetAsDefault ---

    [Fact]
    public void SetAsDefault_SetsIsDefaultTrueAndUpdatesTimestamp()
    {
        var layout = DashboardLayout.Create("D", OwnerId, TenantId);

        layout.SetAsDefault();

        layout.IsDefault.Should().BeTrue();
        layout.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void ClearDefault_SetsIsDefaultFalse()
    {
        var layout = DashboardLayout.Create("D", OwnerId, TenantId, isDefault: true);

        layout.ClearDefault();

        layout.IsDefault.Should().BeFalse();
    }

    // --- UpdateLayout ---

    [Fact]
    public void UpdateLayout_ReplacesWidgetPositionsJson()
    {
        var layout = DashboardLayout.Create("D", OwnerId, TenantId);
        const string positions = "[{\"widgetId\":\"abc\",\"x\":0,\"y\":0,\"w\":6,\"h\":4}]";

        layout.UpdateLayout(positions);

        layout.WidgetPositions.Should().Be(positions);
        layout.UpdatedAt.Should().NotBeNull();
    }

    // --- Rename ---

    [Fact]
    public void Rename_UpdatesNameAndTimestamp()
    {
        var layout = DashboardLayout.Create("Old Name", OwnerId, TenantId);

        layout.Rename("New Name");

        layout.Name.Should().Be("New Name");
        layout.UpdatedAt.Should().NotBeNull();
    }
}
