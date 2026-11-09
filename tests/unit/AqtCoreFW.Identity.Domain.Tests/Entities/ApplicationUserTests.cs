using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using System.Text.Json;

namespace AqtCoreFW.Identity.Domain.Tests.Entities;

/// <summary>
/// Unit tests for ApplicationUser entity.
/// Covers: IsActive flag, PasswordExpiresAt semantics, ExtraProperties bag,
/// ClearanceLevel default, and QĐ742 compliance invariants.
/// </summary>
public sealed class ApplicationUserTests
{
    // --- Defaults ---

    [Fact]
    public void NewUser_IsActiveByDefault()
    {
        var user = new ApplicationUser();
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void NewUser_DefaultClearanceLevel_IsInternal()
    {
        // QĐ742: new accounts start at Internal — must be promoted explicitly
        var user = new ApplicationUser();
        user.ClearanceLevel.Should().Be(ClassificationLevel.Internal);
    }

    [Fact]
    public void NewUser_ExtraProperties_IsEmptyDictionary()
    {
        var user = new ApplicationUser();
        user.ExtraProperties.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void NewUser_PasswordExpiresAt_IsNullByDefault()
    {
        var user = new ApplicationUser();
        user.PasswordExpiresAt.Should().BeNull();
    }

    [Fact]
    public void NewUser_CreatedAtUtc_IsPopulated()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var user = new ApplicationUser();
        var after = DateTime.UtcNow.AddSeconds(1);

        user.CreatedAtUtc.Should().BeAfter(before).And.BeBefore(after);
    }

    // --- IsActive flag ---

    [Fact]
    public void IsActive_CanBeSetToFalse_SoftDeletesUser()
    {
        var user = new ApplicationUser { IsActive = false };
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_CanBeReactivated()
    {
        var user = new ApplicationUser { IsActive = false };
        user.IsActive = true;
        user.IsActive.Should().BeTrue();
    }

    // --- PasswordExpiresAt (QĐ742 password expiry) ---

    [Fact]
    public void PasswordExpiresAt_WhenSetToFuture_IsNotExpired()
    {
        var user = new ApplicationUser
        {
            PasswordExpiresAt = DateTime.UtcNow.AddDays(90)
        };

        var isExpired = user.PasswordExpiresAt.HasValue && user.PasswordExpiresAt.Value < DateTime.UtcNow;
        isExpired.Should().BeFalse();
    }

    [Fact]
    public void PasswordExpiresAt_WhenSetToPast_IsExpired()
    {
        var user = new ApplicationUser
        {
            PasswordExpiresAt = DateTime.UtcNow.AddDays(-1)
        };

        var isExpired = user.PasswordExpiresAt.HasValue && user.PasswordExpiresAt.Value < DateTime.UtcNow;
        isExpired.Should().BeTrue();
    }

    [Fact]
    public void PasswordExpiresAt_WhenNull_IsNotConsideredExpired()
    {
        var user = new ApplicationUser { PasswordExpiresAt = null };

        // Null means "never expires" — typically system accounts or before policy enforcement
        var isExpired = user.PasswordExpiresAt.HasValue && user.PasswordExpiresAt.Value < DateTime.UtcNow;
        isExpired.Should().BeFalse();
    }

    // --- ExtraProperties extension bag ---

    [Fact]
    public void ExtraProperties_CanStoreArbitraryModuleData()
    {
        var user = new ApplicationUser();
        var jsonValue = JsonSerializer.SerializeToElement("EMP-001");

        user.ExtraProperties["hr.employee_code"] = jsonValue;

        user.ExtraProperties.Should().ContainKey("hr.employee_code");
        user.ExtraProperties["hr.employee_code"].GetString().Should().Be("EMP-001");
    }

    [Fact]
    public void ExtraProperties_CanStoreMultipleModuleValues()
    {
        var user = new ApplicationUser();
        user.ExtraProperties["hr.employee_code"] = JsonSerializer.SerializeToElement("EMP-001");
        user.ExtraProperties["finance.cost_center"] = JsonSerializer.SerializeToElement("CC-42");
        user.ExtraProperties["cases.max_open"] = JsonSerializer.SerializeToElement(10);

        user.ExtraProperties.Should().HaveCount(3);
    }

    [Fact]
    public void ExtraProperties_KeyConvention_UsesModulePrefixDotField()
    {
        // Enforces naming convention: "module.field_name" — prevents key collisions between modules
        var user = new ApplicationUser();
        var key = "cases.supervisor_id";

        user.ExtraProperties[key] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString());

        user.ExtraProperties.Keys.First().Should().MatchRegex(@"^\w+\.\w+");
    }

    // --- ClearanceLevel ---

    [Theory]
    [InlineData(ClassificationLevel.Public)]
    [InlineData(ClassificationLevel.Internal)]
    [InlineData(ClassificationLevel.Confidential)]
    [InlineData(ClassificationLevel.Secret)]
    [InlineData(ClassificationLevel.TopSecret)]
    public void ClearanceLevel_CanBeSetToAnyValidLevel(ClassificationLevel level)
    {
        var user = new ApplicationUser { ClearanceLevel = level };
        user.ClearanceLevel.Should().Be(level);
    }

    // --- TenantId ---

    [Fact]
    public void TenantId_IsNullByDefault_SupportingSystemAccountsWithoutTenant()
    {
        var user = new ApplicationUser();
        user.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantId_CanBeAssigned_ForTenantScopedUsers()
    {
        var tenantId = Guid.NewGuid();
        var user = new ApplicationUser { TenantId = tenantId };
        user.TenantId.Should().Be(tenantId);
    }

    // --- FullName ---

    [Fact]
    public void FullName_DefaultsToEmptyString_NotNull()
    {
        var user = new ApplicationUser();
        user.FullName.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void FullName_AcceptsVietnameseCharacters()
    {
        var user = new ApplicationUser { FullName = "Nguyễn Văn An" };
        user.FullName.Should().Be("Nguyễn Văn An");
    }
}
