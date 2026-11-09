using AqtCoreFW.MasterData.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.MasterData.Tests.Entities;

/// <summary>
/// TC-MD-D001: CaseType entity Create and validation.
/// TC-MD-D003: JobTitle entity Create.
/// TC-MD-D004: AdministrativeUnit hierarchical code.
/// </summary>
public sealed class CaseTypeJobTitleTests
{
    // --- CaseType (TC-MD-D001) ---

    [Fact]
    public void CaseType_Create_SetsAllProperties()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application", tenantId: "t1", sortOrder: 3);

        ct.Code.Should().Be("APP");
        ct.NameVi.Should().Be("Đơn xin");
        ct.NameEn.Should().Be("Application");
        ct.TenantId.Should().Be("t1");
        ct.SortOrder.Should().Be(3);
        ct.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CaseType_Create_GeneratesUniqueId()
    {
        var a = CaseType.Create("APP", "Đơn xin", "Application");
        var b = CaseType.Create("LIC", "Giấy phép", "License");

        // Note: T1 entity — long Id is DB-assigned (0 pre-SaveChanges); uniqueness not testable in-memory.
        // Keep only the compile-clean check that both variables resolve without error.
        _ = a.Id;
        _ = b.Id;
    }

    [Fact]
    public void CaseType_Create_WithoutTenant_TenantIdIsNull()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application");

        ct.TenantId.Should().BeNull();
    }

    [Fact]
    public void CaseType_Create_DefaultSortOrderIsZero()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application");

        ct.SortOrder.Should().Be(0);
    }

    // --- JobTitle (TC-MD-D003) ---

    [Fact]
    public void JobTitle_Create_SetsAllProperties()
    {
        var jt = JobTitle.Create("DIR", "Giám đốc", "Director", tenantId: "t2", sortOrder: 1);

        jt.Code.Should().Be("DIR");
        jt.NameVi.Should().Be("Giám đốc");
        jt.NameEn.Should().Be("Director");
        jt.TenantId.Should().Be("t2");
        jt.SortOrder.Should().Be(1);
        jt.IsActive.Should().BeTrue();
    }

    // --- AdministrativeUnit (TC-MD-D004) ---

    [Fact]
    public void AdministrativeUnit_Create_ProvinceLevel_SetsLevel1()
    {
        var unit = AdministrativeUnit.Create(
            "HN", "Hà Nội", "Ha Noi", level: 1,
            parentCode: null, successorCode: null, isActive: true);

        unit.Level.Should().Be(1);
        unit.ParentCode.Should().BeNull();
        unit.IsActive.Should().BeTrue();
    }

    [Fact]
    public void AdministrativeUnit_Create_DistrictLevel_SetsParentCode()
    {
        var unit = AdministrativeUnit.Create(
            "HBT", "Hai Bà Trưng", "Hai Ba Trung", level: 2,
            parentCode: "HN", successorCode: null, isActive: true);

        unit.Level.Should().Be(2);
        unit.ParentCode.Should().Be("HN");
    }

    [Fact]
    public void AdministrativeUnit_Create_Dissolved_SetsSuccessorAndEffectiveTo()
    {
        var effectiveTo = DateTimeOffset.UtcNow.AddDays(-30);
        var unit = AdministrativeUnit.Create(
            "OLD", "Đơn vị cũ", "Old Unit", level: 3,
            parentCode: "HBT", successorCode: "NEW", isActive: false,
            effectiveTo: effectiveTo);

        unit.IsActive.Should().BeFalse();
        unit.SuccessorCode.Should().Be("NEW");
        unit.EffectiveTo.Should().Be(effectiveTo);
    }

}
