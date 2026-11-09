using AqtCoreFW.MasterData.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.MasterData.Tests.Entities;

/// <summary>
/// Unit tests for MasterData administrative unit entities.
/// These entities have factory methods with property assignments — verify the factory
/// wires every field correctly and sets sensible defaults (IsActive=true, SortOrder).
/// </summary>
public sealed class AdministrativeUnitTests
{
    // --- Province ---

    [Fact]
    public void Province_Create_SetsCodeAndNames()
    {
        var province = Province.Create("HN", "Hà Nội", "Ha Noi");

        province.Code.Should().Be("HN");
        province.NameVi.Should().Be("Hà Nội");
        province.NameEn.Should().Be("Ha Noi");
    }

    [Fact]
    public void Province_Create_IsActiveByDefault()
    {
        var province = Province.Create("HN", "Hà Nội", "Ha Noi");

        province.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Province_Create_DefaultSortOrderIsZero()
    {
        var province = Province.Create("HN", "Hà Nội", "Ha Noi");

        province.SortOrder.Should().Be(0);
    }

    [Fact]
    public void Province_Create_WithSortOrder_SetsSortOrder()
    {
        var province = Province.Create("HCM", "TP. Hồ Chí Minh", "Ho Chi Minh City", sortOrder: 5);

        province.SortOrder.Should().Be(5);
    }

    // --- District ---

    [Fact]
    public void District_Create_SetsCodeAndProvinceCode()
    {
        var district = District.Create("Q1", "HCM", "Quận 1", "District 1");

        district.Code.Should().Be("Q1");
        district.ProvinceCode.Should().Be("HCM");
    }

    [Fact]
    public void District_Create_SetsNames()
    {
        var district = District.Create("Q1", "HCM", "Quận 1", "District 1");

        district.NameVi.Should().Be("Quận 1");
        district.NameEn.Should().Be("District 1");
    }

    [Fact]
    public void District_Create_IsActiveByDefault()
    {
        var district = District.Create("Q1", "HCM", "Quận 1", "District 1");

        district.IsActive.Should().BeTrue();
    }

    // --- Ward ---

    [Fact]
    public void Ward_Create_SetsCodeAndDistrictCode()
    {
        var ward = Ward.Create("W01", "Q1", "Phường Bến Nghé", "Ben Nghe Ward");

        ward.Code.Should().Be("W01");
        ward.DistrictCode.Should().Be("Q1");
    }

    [Fact]
    public void Ward_Create_IsActiveByDefault()
    {
        var ward = Ward.Create("W01", "Q1", "Phường Bến Nghé", "Ben Nghe Ward");

        ward.IsActive.Should().BeTrue();
    }

    // --- CaseType ---

    [Fact]
    public void CaseType_Create_SetsCodeAndNames()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application");

        ct.Code.Should().Be("APP");
        ct.NameVi.Should().Be("Đơn xin");
        ct.NameEn.Should().Be("Application");
    }

    [Fact]
    public void CaseType_Create_WithTenantId_SetsTenantId()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application", tenantId: "tenant-1");

        ct.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void CaseType_Create_WithoutTenant_TenantIdIsNull()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application");

        ct.TenantId.Should().BeNull();
    }

    [Fact]
    public void CaseType_Create_IsActiveByDefault()
    {
        var ct = CaseType.Create("APP", "Đơn xin", "Application");

        ct.IsActive.Should().BeTrue();
    }

    // --- JobTitle ---

    [Fact]
    public void JobTitle_Create_SetsCodeAndNames()
    {
        var jt = JobTitle.Create("DIR", "Giám đốc", "Director");

        jt.Code.Should().Be("DIR");
        jt.NameVi.Should().Be("Giám đốc");
        jt.NameEn.Should().Be("Director");
    }

    [Fact]
    public void JobTitle_Create_WithTenantId_SetsTenantId()
    {
        var jt = JobTitle.Create("DIR", "Giám đốc", "Director", tenantId: "tenant-2");

        jt.TenantId.Should().Be("tenant-2");
    }

    [Fact]
    public void JobTitle_Create_IsActiveByDefault()
    {
        var jt = JobTitle.Create("DIR", "Giám đốc", "Director");

        jt.IsActive.Should().BeTrue();
    }
}
