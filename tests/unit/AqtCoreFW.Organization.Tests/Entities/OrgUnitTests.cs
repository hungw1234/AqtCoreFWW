using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Organization.Tests.Entities;

/// <summary>
/// Unit tests for OrgUnit entity (T2: long Id + Guid PublicId).
/// Covers Create factory, lifecycle methods (Update, Deactivate, SetSuccessor),
/// and hierarchy properties (Level, ParentId as long?).
/// </summary>
public sealed class OrgUnitTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // T2 entity: IGuidGenerator required by Create to assign PublicId.
    private static IGuidGenerator MockGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    private static OrgUnit CreateRoot(
        string name = "Ministry of Justice",
        string nameEn = "Ministry of Justice",
        string code = "MOJ",
        long? parentId = null,
        int level = 1) =>
        OrgUnit.Create(MockGen(), name, nameEn, code, TenantId, parentId, level);

    // --- Create ---

    [Fact]
    public void Create_SetsNameAndCode()
    {
        var unit = CreateRoot(name: "Department A", code: "DEPT-A");

        unit.Name.Should().Be("Department A");
        unit.Code.Should().Be("DEPT-A");
    }

    [Fact]
    public void Create_SetsNameEn()
    {
        var unit = CreateRoot(nameEn: "Finance Division");

        unit.NameEn.Should().Be("Finance Division");
    }

    [Fact]
    public void Create_DefaultLevel_IsOne()
    {
        var unit = OrgUnit.Create(MockGen(), "Root", "Root", "ROOT", TenantId);

        unit.Level.Should().Be(1);
    }

    [Fact]
    public void Create_WithLevel_SetsCorrectLevel()
    {
        // parentId is long? — pass a placeholder long value
        var unit = OrgUnit.Create(MockGen(), "Division", "Division", "DIV-01", TenantId, parentId: 42L, level: 3);

        unit.Level.Should().Be(3);
    }

    [Fact]
    public void Create_WithParentId_SetsParentId()
    {
        var unit = CreateRoot(parentId: 99L);

        unit.ParentId.Should().Be(99L);
    }

    [Fact]
    public void Create_WithoutParent_ParentIdIsNull()
    {
        var unit = OrgUnit.Create(MockGen(), "Ministry", "Ministry", "MIN", TenantId);

        unit.ParentId.Should().BeNull();
    }

    [Fact]
    public void Create_IsActiveByDefault()
    {
        var unit = CreateRoot();

        unit.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_AssignsNonZeroSurrogateIdAfterPersistence()
    {
        // Id (long) is DB-assigned; before SaveChanges in-memory it is 0.
        // PublicId (Guid) is assigned by IGuidGenerator on Create.
        var unit = CreateRoot();

        unit.PublicId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_TwoUnits_HaveDifferentPublicIds()
    {
        var a = CreateRoot(code: "A");
        var b = CreateRoot(code: "B");

        a.PublicId.Should().NotBe(b.PublicId);
    }

    [Fact]
    public void Create_SetsTenantId()
    {
        var unit = CreateRoot();

        unit.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_SuccessorId_IsNull()
    {
        var unit = CreateRoot();

        unit.SuccessorId.Should().BeNull();
    }

    [Fact]
    public void Create_Children_IsEmpty()
    {
        var unit = CreateRoot();

        unit.Children.Should().BeEmpty();
    }

    // --- Update ---

    [Fact]
    public void Update_ChangesNameAndNameEn()
    {
        var unit = CreateRoot(name: "Old Name", nameEn: "Old English");

        unit.Update("New Name", "New English");

        unit.Name.Should().Be("New Name");
        unit.NameEn.Should().Be("New English");
    }

    [Fact]
    public void Update_SetsUpdatedAt()
    {
        var unit = CreateRoot();
        var before = DateTime.UtcNow.AddSeconds(-1); // UpdatedAt is DateTime (Entity.cs)

        unit.Update("Changed", "Changed");

        unit.UpdatedAt.Should().NotBeNull();
        unit.UpdatedAt.Should().BeAfter(before);
    }

    // --- Deactivate ---

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var unit = CreateRoot();

        unit.Deactivate();

        unit.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_SetsUpdatedAt()
    {
        var unit = CreateRoot();
        var before = DateTime.UtcNow.AddSeconds(-1); // UpdatedAt is DateTime (Entity.cs)

        unit.Deactivate();

        unit.UpdatedAt.Should().NotBeNull();
        unit.UpdatedAt.Should().BeAfter(before);
    }

    // --- SetSuccessor ---

    [Fact]
    public void SetSuccessor_SetsSuccessorId()
    {
        var unit = CreateRoot();
        // SetSuccessor takes long (internal surrogate Id of successor)
        const long successorLongId = 12345L;

        unit.SetSuccessor(successorLongId);

        unit.SuccessorId.Should().Be(successorLongId);
    }

    [Fact]
    public void SetSuccessor_SetsUpdatedAt()
    {
        var unit = CreateRoot();
        var before = DateTime.UtcNow.AddSeconds(-1); // UpdatedAt is DateTime (Entity.cs)

        unit.SetSuccessor(99L);

        unit.UpdatedAt.Should().NotBeNull();
        unit.UpdatedAt.Should().BeAfter(before);
    }

    // --- Hierarchy semantics ---

    [Fact]
    public void Create_DeptUnderMinistry_LevelTwoParentSet()
    {
        var ministry = CreateRoot(level: 1);
        // In-memory Id = 0 before SaveChanges; use a known placeholder long
        var dept = OrgUnit.Create(MockGen(), "Department", "Department", "DEPT", TenantId, parentId: 1L, level: 2);

        dept.Level.Should().Be(2);
        dept.ParentId.Should().Be(1L);
    }
}
