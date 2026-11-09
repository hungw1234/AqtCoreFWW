using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for ViewColumn factory and validation.
/// ViewColumn is T1: long Id (DB-assigned). ViewDefinitionId=long (FK to T1 ViewDefinition).
/// </summary>
public sealed class ViewColumnTests
{
    // T1 entity — use a constant representing the FK long value
    private const long ViewId = 1L;

    [Fact]
    public void Create_WithValidInputs_ReturnsColumnWithCorrectProperties()
    {
        var col = ViewColumn.Create(
            ViewId, "Status", "Status Label", 1,
            sortable: true, filterable: true,
            width: 120, formatter: ColumnFormatter.Badge);

        col.Should().NotBeNull();
        // T1 Id is DB-assigned; before save it is 0 — object existence is the invariant
        col.Id.Should().BeGreaterThanOrEqualTo(0);
        col.ViewDefinitionId.Should().Be(ViewId);
        col.FieldName.Should().Be("Status");
        col.Label.Should().Be("Status Label");
        col.DisplayOrder.Should().Be(1);
        col.Sortable.Should().BeTrue();
        col.Filterable.Should().BeTrue();
        col.Width.Should().Be(120);
        col.Formatter.Should().Be(ColumnFormatter.Badge);
    }

    [Fact]
    public void Create_WithDefaults_AppliesTextFormatterAndFalseFlags()
    {
        var col = ViewColumn.Create(ViewId, "Name", "Name", 1);

        col.Sortable.Should().BeFalse();
        col.Filterable.Should().BeFalse();
        col.Width.Should().BeNull();
        col.Formatter.Should().Be(ColumnFormatter.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyFieldName_Throws(string fieldName)
    {
        var act = () => ViewColumn.Create(ViewId, fieldName, "Label", 1);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyLabel_Throws(string label)
    {
        var act = () => ViewColumn.Create(ViewId, "field", label, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ReturnsTwoDistinctObjects()
    {
        // T1 Ids are DB-assigned; before save both are 0, but objects are distinct references
        var c1 = ViewColumn.Create(ViewId, "F1", "L1", 1);
        var c2 = ViewColumn.Create(ViewId, "F2", "L2", 2);
        c1.Should().NotBeSameAs(c2);
        c1.FieldName.Should().NotBe(c2.FieldName);
    }
}
