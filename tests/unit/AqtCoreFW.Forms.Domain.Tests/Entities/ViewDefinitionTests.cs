using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for ViewDefinition aggregate root.
/// Covers factory creation, SetAsDefault, AddColumn, AddFilter, and validation guards.
/// </summary>
public sealed class ViewDefinitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    // --- Factory ---

    [Fact]
    public void Create_WithValidInputs_ReturnsViewWithCorrectProperties()
    {
        var view = ViewDefinition.Create(
            "My List", "Case", ViewType.List, TenantId, ActorId,
            defaultSortField: "CreatedAt", defaultSortDir: "desc",
            pageSize: 25, isDefault: true);

        view.Should().NotBeNull();
        // T1 entity: Id is DB-assigned long; before save it is 0 (default). Object non-null is the invariant.
        view.Id.Should().BeGreaterThanOrEqualTo(0);
        view.TenantId.Should().Be(TenantId);
        view.Name.Should().Be("My List");
        view.EntityType.Should().Be("Case");
        view.Type.Should().Be(ViewType.List);
        view.DefaultSortField.Should().Be("CreatedAt");
        view.DefaultSortDir.Should().Be("desc");
        view.PageSize.Should().Be(25);
        view.IsDefault.Should().BeTrue();
        view.Columns.Should().BeEmpty();
        view.Filters.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithDefaultArguments_AppliesDefaults()
    {
        var view = ViewDefinition.Create("V", "Task", ViewType.Grid, TenantId, ActorId);

        view.PageSize.Should().Be(20);
        view.IsDefault.Should().BeFalse();
        view.DefaultSortField.Should().BeNull();
        view.DefaultSortDir.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string name)
    {
        var act = () => ViewDefinition.Create(name, "Case", ViewType.List, TenantId, ActorId);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyEntityType_Throws(string entityType)
    {
        var act = () => ViewDefinition.Create("Name", entityType, ViewType.List, TenantId, ActorId);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithZeroPageSize_Throws()
    {
        var act = () => ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId, pageSize: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("up")]
    [InlineData("DESC")]
    [InlineData("ascending")]
    public void Create_WithInvalidSortDir_Throws(string dir)
    {
        var act = () => ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId, defaultSortDir: dir);
        act.Should().Throw<ArgumentException>();
    }

    // --- SetAsDefault ---

    [Fact]
    public void SetAsDefault_SetsIsDefaultTrue()
    {
        var view = ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId);
        view.IsDefault.Should().BeFalse();

        view.SetAsDefault(ActorId);

        view.IsDefault.Should().BeTrue();
    }

    // --- AddColumn ---

    [Fact]
    public void AddColumn_AppendsColumnToCollection()
    {
        var view = ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId);
        var col = ViewColumn.Create(view.Id, "Status", "Status", 1);

        view.AddColumn(col);

        view.Columns.Should().HaveCount(1);
        view.Columns[0].FieldName.Should().Be("Status");
    }

    [Fact]
    public void AddColumn_WithNull_Throws()
    {
        var view = ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId);
        var act = () => view.AddColumn(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddColumn_MultipleColumns_PreservesOrder()
    {
        var view = ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId);
        view.AddColumn(ViewColumn.Create(view.Id, "A", "A", 1));
        view.AddColumn(ViewColumn.Create(view.Id, "B", "B", 2));

        view.Columns.Should().HaveCount(2);
        view.Columns[0].FieldName.Should().Be("A");
        view.Columns[1].FieldName.Should().Be("B");
    }

    // --- AddFilter ---

    [Fact]
    public void AddFilter_AppendsFilterToCollection()
    {
        var view = ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId);
        var filter = ViewFilter.Create(view.Id, "Status", FilterOperator.Equals, "Open");

        view.AddFilter(filter);

        view.Filters.Should().HaveCount(1);
        view.Filters[0].FieldName.Should().Be("Status");
    }

    [Fact]
    public void AddFilter_WithNull_Throws()
    {
        var view = ViewDefinition.Create("V", "E", ViewType.List, TenantId, ActorId);
        var act = () => view.AddFilter(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
