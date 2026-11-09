using AqtCoreFW.Identity.Infrastructure.Services;
using FluentAssertions;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for MenuService.BuildAndPruneTree (two-pass tree filter — F4/F10 fix).
/// Tests the internal static method directly via InternalsVisibleTo.
/// Scenarios:
///   - Leaf node included when user has required permission
///   - Leaf node excluded when user lacks required permission
///   - Public (no permission required) leaf always included
///   - Parent kept when authorized child exists (F4: parent had no perm, child does)
///   - Group node removed when ALL children are filtered (F10: empty group headers)
///   - Nested subtree: only authorized branches survive
/// </summary>
public sealed class MenuServiceFilterTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CachedMenuEntry Leaf(
        Guid id, string code, string title,
        Guid? parentId = null, List<string>? perms = null, int sort = 0)
        => new(id, parentId, code, title, null, null, $"/{code}", sort, perms ?? []);

    private static CachedMenuEntry Group(
        Guid id, string code, string title,
        Guid? parentId = null, List<string>? perms = null, int sort = 0)
        => new(id, parentId, code, title, null, null, null, sort, perms ?? []);

    private static IReadOnlySet<string> Perms(params string[] codes)
        => new HashSet<string>(codes, StringComparer.Ordinal);

    // ── Happy path: public leaf ───────────────────────────────────────────────

    [Fact]
    public void PublicLeaf_NoPermRequired_AlwaysIncluded()
    {
        var id = Guid.NewGuid();
        var menus = new List<CachedMenuEntry> { Leaf(id, "dashboard", "Dashboard") };

        var result = MenuService.BuildAndPruneTree(menus, Perms(), parentId: null);

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("dashboard");
    }

    // ── Permission-gated leaf ─────────────────────────────────────────────────

    [Fact]
    public void GatedLeaf_UserHasPermission_Included()
    {
        var id = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Leaf(id, "roles", "Roles", perms: ["IDENTITY.ROLE.VIEW"])
        };

        var result = MenuService.BuildAndPruneTree(menus, Perms("IDENTITY.ROLE.VIEW"), null);

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("roles");
    }

    [Fact]
    public void GatedLeaf_UserLacksPermission_Excluded()
    {
        var id = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Leaf(id, "roles", "Roles", perms: ["IDENTITY.ROLE.VIEW"])
        };

        var result = MenuService.BuildAndPruneTree(menus, Perms("CASES.CASE.VIEW"), null);

        result.Should().BeEmpty();
    }

    // ── F4: Parent kept when authorized child exists ──────────────────────────
    // Scenario: parent has no permission gate, child has gate user holds.
    // Before fix: parent filtered out first → child orphaned and lost.
    // After fix (two-pass): child is authorized → parent preserved as container.

    [Fact]
    public void F4_ParentNoGate_AuthorizedChild_BothKept()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Group(parentId, "admin", "Admin"),
            Leaf(childId, "users", "Users", parentId: parentId, perms: ["IDENTITY.USER.VIEW"])
        };

        var result = MenuService.BuildAndPruneTree(menus, Perms("IDENTITY.USER.VIEW"), null);

        result.Should().HaveCount(1, "parent group should be kept because child is authorized");
        result[0].Code.Should().Be("admin");
        result[0].Children.Should().HaveCount(1);
        result[0].Children[0].Code.Should().Be("users");
    }

    [Fact]
    public void F4_ParentNoGate_UnauthorizedChild_ParentRemoved()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Group(parentId, "admin", "Admin"),
            Leaf(childId, "users", "Users", parentId: parentId, perms: ["IDENTITY.USER.VIEW"])
        };

        // User has no permissions — child filtered, parent should also be removed
        var result = MenuService.BuildAndPruneTree(menus, Perms(), null);

        result.Should().BeEmpty("group with no authorized children should be pruned");
    }

    // ── F10: Empty group headers removed ─────────────────────────────────────
    // Group node with ALL children filtered → group itself removed.

    [Fact]
    public void F10_GroupWithAllChildrenFiltered_GroupRemoved()
    {
        var grpId = Guid.NewGuid();
        var child1 = Guid.NewGuid();
        var child2 = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Group(grpId, "identity-group", "Identity"),
            Leaf(child1, "users", "Users", parentId: grpId, perms: ["IDENTITY.USER.VIEW"]),
            Leaf(child2, "roles", "Roles", parentId: grpId, perms: ["IDENTITY.ROLE.VIEW"])
        };

        // User has neither — both children filtered, group should disappear
        var result = MenuService.BuildAndPruneTree(menus, Perms("CASES.CASE.VIEW"), null);

        result.Should().BeEmpty("group node must not render as empty header when all children filtered");
    }

    [Fact]
    public void F10_GroupPartialChildrenFiltered_GroupKeptWithVisibleChildren()
    {
        var grpId = Guid.NewGuid();
        var child1 = Guid.NewGuid();
        var child2 = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Group(grpId, "identity-group", "Identity"),
            Leaf(child1, "users", "Users", parentId: grpId, perms: ["IDENTITY.USER.VIEW"]),
            Leaf(child2, "roles", "Roles", parentId: grpId, perms: ["IDENTITY.ROLE.VIEW"])
        };

        // User has only USER.VIEW — one child visible, group kept with 1 child
        var result = MenuService.BuildAndPruneTree(menus, Perms("IDENTITY.USER.VIEW"), null);

        result.Should().HaveCount(1);
        result[0].Children.Should().HaveCount(1);
        result[0].Children[0].Code.Should().Be("users");
    }

    // ── Multi-level nesting ───────────────────────────────────────────────────

    [Fact]
    public void DeepNesting_OnlyAuthorizedBranchSurvives()
    {
        var rootId = Guid.NewGuid();
        var grpAId = Guid.NewGuid();
        var grpBId = Guid.NewGuid();
        var leafAId = Guid.NewGuid();
        var leafBId = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Group(rootId, "root", "Root"),
            Group(grpAId, "grpA", "Group A", parentId: rootId),
            Group(grpBId, "grpB", "Group B", parentId: rootId),
            Leaf(leafAId, "leafA", "Leaf A", parentId: grpAId, perms: ["PERM.A"]),
            Leaf(leafBId, "leafB", "Leaf B", parentId: grpBId, perms: ["PERM.B"]),
        };

        // User only has PERM.B — grpA+leafA filtered, grpB+leafB kept
        var result = MenuService.BuildAndPruneTree(menus, Perms("PERM.B"), null);

        result.Should().HaveCount(1, "root kept because grpB has authorized child");
        result[0].Children.Should().HaveCount(1, "only grpB survives");
        result[0].Children[0].Code.Should().Be("grpB");
        result[0].Children[0].Children.Should().HaveCount(1);
        result[0].Children[0].Children[0].Code.Should().Be("leafB");
    }

    // ── Sort order preserved ──────────────────────────────────────────────────

    [Fact]
    public void SortOrder_PreservedInOutput()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var menus = new List<CachedMenuEntry>
        {
            Leaf(id3, "c", "C", sort: 30),
            Leaf(id1, "a", "A", sort: 10),
            Leaf(id2, "b", "B", sort: 20),
        };

        var result = MenuService.BuildAndPruneTree(menus, Perms(), null);

        result.Should().HaveCount(3);
        result[0].Code.Should().Be("a");
        result[1].Code.Should().Be("b");
        result[2].Code.Should().Be("c");
    }
}
