using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for ViewFilter factory and validation.
/// ViewFilter is T1: long Id (DB-assigned). ViewDefinitionId=long (FK to T1 ViewDefinition).
/// </summary>
public sealed class ViewFilterTests
{
    // T1 entity — use a constant representing the FK long value
    private const long ViewId = 1L;

    [Fact]
    public void Create_WithValidInputs_ReturnsFilterWithCorrectProperties()
    {
        var filter = ViewFilter.Create(
            ViewId, "Status", FilterOperator.Equals,
            defaultValue: "Open", isRequired: true);

        filter.Should().NotBeNull();
        // T1 Id is DB-assigned; before save it is 0 — object existence is the invariant
        filter.Id.Should().BeGreaterThanOrEqualTo(0);
        filter.ViewDefinitionId.Should().Be(ViewId);
        filter.FieldName.Should().Be("Status");
        filter.Operator.Should().Be(FilterOperator.Equals);
        filter.DefaultValue.Should().Be("Open");
        filter.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Create_WithDefaults_HasNullDefaultValueAndNotRequired()
    {
        var filter = ViewFilter.Create(ViewId, "Status", FilterOperator.Contains);

        filter.DefaultValue.Should().BeNull();
        filter.IsRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyFieldName_Throws(string fieldName)
    {
        var act = () => ViewFilter.Create(ViewId, fieldName, FilterOperator.Equals);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ReturnsTwoDistinctObjects()
    {
        // T1 Ids are DB-assigned; before save both are 0, but objects are distinct references
        var f1 = ViewFilter.Create(ViewId, "F1", FilterOperator.Equals);
        var f2 = ViewFilter.Create(ViewId, "F2", FilterOperator.Contains);
        f1.Should().NotBeSameAs(f2);
        f1.FieldName.Should().NotBe(f2.FieldName);
    }

    [Theory]
    [InlineData(FilterOperator.Equals)]
    [InlineData(FilterOperator.Contains)]
    [InlineData(FilterOperator.GreaterThan)]
    [InlineData(FilterOperator.LessThan)]
    [InlineData(FilterOperator.Between)]
    [InlineData(FilterOperator.In)]
    public void Create_AllOperators_Succeed(FilterOperator op)
    {
        var act = () => ViewFilter.Create(ViewId, "Field", op);
        act.Should().NotThrow();
    }
}
