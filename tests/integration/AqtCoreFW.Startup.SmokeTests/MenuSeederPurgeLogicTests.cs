using AqtCoreFW.Api;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Fast, dependency-free unit tests for the pure pieces of <see cref="E2ETestDataSeeder"/>'s
/// menu orphan-purge [MTS-3]: the seeded-family predicate and the leaf-first deletion layering.
/// (The end-to-end behaviour against a real DB is covered by <see cref="MenuSeederOrphanPurgeSmokeTests"/>.)
/// No <c>[Trait("Category","Smoke")]</c> — these run in the normal fast-feedback pass.
/// </summary>
public sealed class MenuSeederPurgeLogicTests
{
    [Theory]
    [InlineData("60000000-0000-0000-0000-000000000030", true)]   // canonical seeded form
    [InlineData("60000000-0000-0000-0000-0000000000FF", true)]   // upper-case hex — must still match
    [InlineData("60000000-0000-0000-0000-FFFFFFFFFFFF", true)]
    [InlineData("7f000000-0000-0000-0000-000000000001", false)]  // admin-created (Guid v7) — out of family
    [InlineData("00000000-0000-0000-0000-000000000001", false)]
    [InlineData("60000001-0000-0000-0000-000000000001", false)]  // first segment differs
    public void IsSeededMenuFamily_MatchesOnlyThePinnedPrefix(string guid, bool expected)
        => E2ETestDataSeeder.IsSeededMenuFamily(Guid.Parse(guid)).Should().Be(expected);

    [Fact]
    public void OrderOrphanMenusLeafFirst_PutsChildrenBeforeParents()
    {
        var grp   = Guid.NewGuid();
        var leaf1 = Guid.NewGuid();
        var leaf2 = Guid.NewGuid();
        var standalone = Guid.NewGuid();

        // grp ← leaf1, leaf2 ; standalone has no parent and is no-one's parent
        var orphans = new[]
        {
            (Id: grp,        Parent: (Guid?)null),
            (Id: leaf1,      Parent: (Guid?)grp),
            (Id: leaf2,      Parent: (Guid?)grp),
            (Id: standalone, Parent: (Guid?)null),
        };

        var layers = E2ETestDataSeeder.OrderOrphanMenusLeafFirst(orphans, o => o.Id, o => o.Parent);

        layers.Should().HaveCount(2);
        layers[0].Select(o => o.Id).Should().BeEquivalentTo(new[] { leaf1, leaf2, standalone });
        layers[1].Select(o => o.Id).Should().BeEquivalentTo(new[] { grp });
        layers.SelectMany(l => l).Should().HaveCount(orphans.Length, "every orphan must land in exactly one layer");
    }

    [Fact]
    public void OrderOrphanMenusLeafFirst_HandlesAChainBottomUp()
    {
        var a = Guid.NewGuid(); // root
        var b = Guid.NewGuid(); // child of a
        var c = Guid.NewGuid(); // child of b

        var orphans = new[]
        {
            (Id: a, Parent: (Guid?)null),
            (Id: b, Parent: (Guid?)a),
            (Id: c, Parent: (Guid?)b),
        };

        var layers = E2ETestDataSeeder.OrderOrphanMenusLeafFirst(orphans, o => o.Id, o => o.Parent);

        layers.Should().HaveCount(3);
        layers[0].Single().Id.Should().Be(c);
        layers[1].Single().Id.Should().Be(b);
        layers[2].Single().Id.Should().Be(a);
    }

    [Fact]
    public void OrderOrphanMenusLeafFirst_DropsCyclicRows_SoCallerCanReportTheGap()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();

        // x ↔ y mutual parents — impossible for a real menu tree, but must not spin.
        var orphans = new[]
        {
            (Id: x, Parent: (Guid?)y),
            (Id: y, Parent: (Guid?)x),
        };

        var layers = E2ETestDataSeeder.OrderOrphanMenusLeafFirst(orphans, o => o.Id, o => o.Parent);

        layers.Should().BeEmpty("cyclic rows belong to no layer");
    }

    [Fact]
    public void OrderOrphanMenusLeafFirst_EmptyInput_YieldsNoLayers()
        => E2ETestDataSeeder.OrderOrphanMenusLeafFirst(
                Array.Empty<(Guid Id, Guid? Parent)>(), o => o.Id, o => o.Parent)
            .Should().BeEmpty();
}
