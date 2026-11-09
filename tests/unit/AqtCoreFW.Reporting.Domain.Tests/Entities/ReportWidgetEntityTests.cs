using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;

namespace AqtCoreFW.Reporting.Domain.Tests.Entities;

/// <summary>
/// Unit tests for ReportWidget entity.
/// Validates Create factory, UpdateQuery, and UpdateConfig behaviour.
/// </summary>
public sealed class ReportWidgetEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    // --- Create ---

    [Fact]
    public void Create_WithValidArgs_SetsAllProperties()
    {
        var widget = ReportWidget.Create(
            name: "Forms by Status",
            type: WidgetType.BarChart,
            dataQuery: "SELECT Status, COUNT(*) FROM forms.FormTemplates WHERE TenantId = @TenantId GROUP BY Status",
            tenantId: TenantId,
            createdBy: CreatedBy,
            config: "{\"color\":\"blue\"}",
            refreshSeconds: 120);

        // T1 entity: Id is long assigned by EF on SaveChanges (0 before persisted)
        widget.Id.Should().Be(0L);
        widget.Name.Should().Be("Forms by Status");
        widget.Type.Should().Be(WidgetType.BarChart);
        widget.TenantId.Should().Be(TenantId);
        widget.Config.Should().Be("{\"color\":\"blue\"}");
        widget.RefreshSeconds.Should().Be(120);
        widget.CreatedBy.Should().Be(CreatedBy);
        widget.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_WithDefaults_UsesRefreshSeconds300()
    {
        var widget = ReportWidget.Create("KPI Widget", WidgetType.KPI,
            "SELECT COUNT(*) FROM forms.FormTemplates WHERE TenantId = @TenantId",
            TenantId, CreatedBy);

        widget.RefreshSeconds.Should().Be(300);
        widget.Config.Should().BeNull();
    }

    // --- UpdateQuery ---

    [Fact]
    public void UpdateQuery_ChangesDataQueryAndSetsUpdatedAt()
    {
        var widget = ReportWidget.Create("W", WidgetType.LineChart,
            "SELECT 1 WHERE @TenantId IS NOT NULL", TenantId, CreatedBy);
        var before = widget.UpdatedAt;

        widget.UpdateQuery("SELECT 2 WHERE @TenantId IS NOT NULL");

        widget.DataQuery.Should().Be("SELECT 2 WHERE @TenantId IS NOT NULL");
        widget.UpdatedAt.Should().NotBeNull();
        widget.UpdatedAt.Should().NotBe(before);
    }

    // --- UpdateConfig ---

    [Fact]
    public void UpdateConfig_ChangesConfigAndSetsUpdatedAt()
    {
        var widget = ReportWidget.Create("W", WidgetType.PieChart,
            "SELECT 1 WHERE @TenantId IS NOT NULL", TenantId, CreatedBy);

        widget.UpdateConfig("{\"theme\":\"dark\"}");

        widget.Config.Should().Be("{\"theme\":\"dark\"}");
        widget.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateConfig_WithNull_ClearsConfig()
    {
        var widget = ReportWidget.Create("W", WidgetType.KPI,
            "SELECT 1 WHERE @TenantId IS NOT NULL", TenantId, CreatedBy,
            config: "{\"initial\":true}");

        widget.UpdateConfig(null);

        widget.Config.Should().BeNull();
    }

    // --- UpdateDetails ---

    [Fact]
    public void UpdateDetails_WithNewName_UpdatesNameOnly()
    {
        var widget = ReportWidget.Create("Old", WidgetType.Table,
            "SELECT 1 WHERE @TenantId IS NOT NULL", TenantId, CreatedBy,
            refreshSeconds: 60);

        widget.UpdateDetails("New Name", null);

        widget.Name.Should().Be("New Name");
        widget.RefreshSeconds.Should().Be(60); // unchanged
    }
}
