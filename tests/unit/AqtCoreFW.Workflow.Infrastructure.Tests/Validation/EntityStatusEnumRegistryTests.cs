using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Contracts.Validation;
using AqtCoreFW.Workflow.Infrastructure.Validation;
using FluentAssertions;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Validation;

/// <summary>
/// Unit tests for EntityStatusEnumRegistry — validates registry behavior
/// including contributor resolution, case-sensitivity, and unknown entity handling.
/// Lives in Infrastructure.Tests because it tests the concrete impl.
/// </summary>
public sealed class EntityStatusEnumRegistryTests
{
    private sealed class FakeCaseContributor : IEntityStatusEnumContributor
    {
        public string EntityType => "Case";
        public IReadOnlyCollection<string> GetStateNames() =>
            ["Draft", "Submitted", "UnderReview", "ReturnedForRevision", "Approved", "Rejected", "Closed"];
    }

    private sealed class FakeFormContributor : IEntityStatusEnumContributor
    {
        public string EntityType => "Form";
        public IReadOnlyCollection<string> GetStateNames() => ["Created", "Submitted", "Processed"];
    }

    [Fact]
    public void IsKnownEntityType_WithRegisteredType_ReturnsTrue()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        registry.IsKnownEntityType("Case").Should().BeTrue();
    }

    [Fact]
    public void IsKnownEntityType_WithUnregisteredType_ReturnsFalse()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        registry.IsKnownEntityType("Unknown").Should().BeFalse();
    }

    [Fact]
    public void IsKnownEntityType_WithNullOrEmpty_ReturnsFalse()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        registry.IsKnownEntityType(null!).Should().BeFalse();
        registry.IsKnownEntityType("").Should().BeFalse();
        registry.IsKnownEntityType("   ").Should().BeFalse();
    }

    [Fact]
    public void GetAllowedStateNames_ForKnownEntityType_ReturnsStateNames()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        var names = registry.GetAllowedStateNames("Case");

        names.Should().BeEquivalentTo(
            ["Draft", "Submitted", "UnderReview", "ReturnedForRevision", "Approved", "Rejected", "Closed"]);
    }

    [Fact]
    public void GetAllowedStateNames_ForUnknownEntityType_ReturnsEmptyCollection()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        var names = registry.GetAllowedStateNames("Unknown");

        names.Should().BeEmpty();
    }

    [Fact]
    public void GetAllowedStateNames_ForNullOrEmpty_ReturnsEmptyCollection()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        registry.GetAllowedStateNames(null!).Should().BeEmpty();
        registry.GetAllowedStateNames("").Should().BeEmpty();
    }

    [Fact]
    public void GetKnownEntityTypes_ReturnsAllRegisteredTypes()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor(), new FakeFormContributor()]);

        var types = registry.GetKnownEntityTypes();

        types.Should().BeEquivalentTo(["Case", "Form"]);
    }

    [Fact]
    public void GetKnownEntityTypes_WithNoContributors_ReturnsEmptyCollection()
    {
        var registry = new EntityStatusEnumRegistry([]);

        registry.GetKnownEntityTypes().Should().BeEmpty();
    }

    [Fact]
    public void IsKnownEntityType_CaseSensitive_DifferentCaseReturnsFalse()
    {
        var registry = new EntityStatusEnumRegistry([new FakeCaseContributor()]);

        // EntityType is case-sensitive — "case" (lowercase) should NOT match "Case"
        registry.IsKnownEntityType("case").Should().BeFalse();
        registry.IsKnownEntityType("CASE").Should().BeFalse();
    }
}
