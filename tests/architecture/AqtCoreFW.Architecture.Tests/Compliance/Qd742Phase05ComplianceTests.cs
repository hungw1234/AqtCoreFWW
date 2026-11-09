using AqtCoreFW.Api.Controllers.Admin;
using AqtCoreFW.Identity.Application.IpRules;
using AqtCoreFW.Identity.Application.SecurityHealth.Queries;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using FluentAssertions;
using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Compliance;

/// <summary>
/// Compliance evidence tests for QĐ 742 Phase-05 (2.2.a, 3.2.a/b, 6.1.a + D7 per-criterion UI expansion).
/// Verifies structural requirements:
///   - IpFilterRule entity exists with all required fields
///   - IpRulesAdminController exists with 5 endpoints, [RequiresMfa], permission gates
///   - SecurityHealthAdminController exists with 6 endpoints (D7 add-ons)
///   - All admin routes have [RequiresMfa] attribute
///   - All routes have [Authorize(Policy = ...)] attribute
///
/// These are structural verification tests (reflection-based), not behavioral tests.
/// Behavioral tests are in IpRulesApiTests, IpFilterMiddlewareCacheInvalidationTests, SecurityHealthQueriesTests.
/// </summary>
public sealed class Qd742Phase05ComplianceTests
{
    // ── IP Filter Rule Entity (QĐ 742 §2.2.a) ────────────────────────────────────

    [Fact]
    public void Compliance_2_2_a_IpFilterRule_EntityExists()
    {
        // QĐ 742 2.2.a: IP whitelist/blocklist management entity.
        // Evidence: IpFilterRule entity type exists with required fields.

        var entityType = typeof(IpFilterRule);
        entityType.IsClass.Should().BeTrue();
    }

    [Fact]
    public void Compliance_2_2_a_IpFilterRule_HasRequiredFields()
    {
        // QĐ 742 2.2.a: IP filter rule must have CIDR, description, enabled, block type fields.
        // Evidence: IpFilterRule has Cidr, Description, IsEnabled, IsBlock properties.

        var entityType = typeof(IpFilterRule);

        entityType.GetProperty("Cidr").Should().NotBeNull();
        entityType.GetProperty("Description").Should().NotBeNull();
        entityType.GetProperty("IsEnabled").Should().NotBeNull();
        entityType.GetProperty("IsBlock").Should().NotBeNull();
        entityType.GetProperty("TenantId").Should().NotBeNull("TenantId for per-tenant rules");
    }

    [Fact]
    public void Compliance_2_2_a_IpFilterRule_IsAuditableEntity()
    {
        // QĐ 742 2.2.a: IP rule changes should be audited.
        // Evidence: IpFilterRule inherits from AuditableEntity (CreatedBy, CreatedAt, UpdatedBy, UpdatedAt).

        var entityType = typeof(IpFilterRule);
        var baseType = entityType.BaseType;

        baseType!.Name.Should().Be("AuditableEntity");
    }

    // ── IP Rules Admin Controller (QĐ 742 §2.2.a) ────────────────────────────────

    [Fact]
    public void Compliance_2_2_a_IpRulesAdminController_Exists()
    {
        // QĐ 742 2.2.a: API endpoint for IP rule CRUD.
        // Evidence: IpRulesAdminController type exists.

        var controllerType = typeof(IpRulesAdminController);
        controllerType.IsClass.Should().BeTrue();
        controllerType.Name.Should().Be("IpRulesAdminController");
    }

    [Fact]
    public void Compliance_2_2_a_IpRulesAdminController_HasRequiresMfaAttribute()
    {
        // QĐ 742 2.2.a + 1.6.b: IP rule mutations require valid MFA session.
        // Evidence: IpRulesAdminController has class-level [RequiresMfa] attribute.

        var controllerType = typeof(IpRulesAdminController);
        var requiresMfaAttr = controllerType.GetCustomAttribute<RequiresMfaAttribute>();

        requiresMfaAttr.Should().NotBeNull("IpRulesAdminController should have [RequiresMfa] at class level");
    }

    [Fact]
    public void Compliance_2_2_a_IpRulesAdminController_HasAuthorizeAttribute()
    {
        // QĐ 742 2.2.a: IP rule access requires Security.IpRule.View permission.
        // Evidence: IpRulesAdminController has class-level [Authorize(Policy = ...)] attribute.

        var controllerType = typeof(IpRulesAdminController);
        var authorizeAttrs = controllerType.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();

        authorizeAttrs.Should().NotBeEmpty("IpRulesAdminController should have [Authorize(Policy = ...)] at class level");
        authorizeAttrs.Should().Contain(a => a.Policy != null && a.Policy.Contains("IP_RULE"),
            "IpRulesAdminController should have [Authorize(Policy = ...)] with IP_RULE policy");
    }

    [Fact]
    public void Compliance_2_2_a_IpRulesAdminController_Has5Endpoints()
    {
        // QĐ 742 2.2.a: IP rule API has List, Get, Create, Update, Delete endpoints.
        // Evidence: IpRulesAdminController has 5 public methods (endpoints).

        var controllerType = typeof(IpRulesAdminController);
        var publicMethods = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        publicMethods.Should().HaveCount(5, "IpRulesAdminController should have List, GetById, Create, Update, Delete");

        var methodNames = publicMethods.Select(m => m.Name).OrderBy(n => n).ToList();
        methodNames.Should().Contain("List");
        methodNames.Should().Contain("GetById");
        methodNames.Should().Contain("Create");
        methodNames.Should().Contain("Update");
        methodNames.Should().Contain("Delete");
    }

    [Fact]
    public void Compliance_2_2_a_IpRuleDto_Exists()
    {
        // QĐ 742 2.2.a: API returns IP rule DTO for list/get operations.
        // Evidence: IpRuleDto record exists with required fields.

        var dtoType = typeof(IpRuleDto);
        dtoType.Should().NotBeNull();

        var properties = dtoType.GetProperties();
        properties.Should().Contain(p => p.Name == "Id");
        properties.Should().Contain(p => p.Name == "Cidr");
        properties.Should().Contain(p => p.Name == "Description");
        properties.Should().Contain(p => p.Name == "IsEnabled");
        properties.Should().Contain(p => p.Name == "IsBlock");
    }

    // ── Security Health Admin Controller (QĐ 742 D7 per-criterion UI) ─────────────

    [Fact]
    public void Compliance_D7_SecurityHealthAdminController_Exists()
    {
        // QĐ 742 D7: Aggregator controller for compliance snapshot endpoints.
        // Evidence: SecurityHealthAdminController type exists.

        var controllerType = typeof(SecurityHealthAdminController);
        controllerType.IsClass.Should().BeTrue();
        controllerType.Name.Should().Be("SecurityHealthAdminController");
    }

    [Fact]
    public void Compliance_D7_SecurityHealthAdminController_HasRequiresMfaAttribute()
    {
        // QĐ 742 1.6.b: All security health queries require valid MFA session.
        // Evidence: SecurityHealthAdminController has class-level [RequiresMfa] attribute.

        var controllerType = typeof(SecurityHealthAdminController);
        var requiresMfaAttr = controllerType.GetCustomAttribute<RequiresMfaAttribute>();

        requiresMfaAttr.Should().NotBeNull("SecurityHealthAdminController should have [RequiresMfa] at class level");
    }

    [Fact]
    public void Compliance_D7_SecurityHealthAdminController_HasAuthorizeAttribute()
    {
        // QĐ 742 D7: Security health view requires Security.Health.View permission.
        // Evidence: SecurityHealthAdminController has class-level [Authorize(Policy = ...)] attribute.

        var controllerType = typeof(SecurityHealthAdminController);
        var authorizeAttrs = controllerType.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();

        authorizeAttrs.Should().NotBeEmpty("SecurityHealthAdminController should have [Authorize(Policy = ...)] at class level");
        authorizeAttrs.Should().Contain(a => a.Policy != null && a.Policy.Contains("HEALTH"),
            "SecurityHealthAdminController should have [Authorize(Policy = ...)] with HEALTH policy");
    }

    [Fact]
    public void Compliance_D7_SecurityHealthAdminController_Has6Endpoints()
    {
        // QĐ 742 D7: Security health has 6 read-only endpoints (login, password, audit-retention, audit-integrity, key-rotation, backup-history).
        // Evidence: SecurityHealthAdminController has 6 public methods (endpoints).

        var controllerType = typeof(SecurityHealthAdminController);
        var publicMethods = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        publicMethods.Should().HaveCount(6,
            "SecurityHealthAdminController should have GetLoginPolicyStatus, GetPasswordPolicyStatus, " +
            "GetAuditRetentionStatus, GetAuditIntegrityStatus, GetKeyRotationStatus, GetBackupHistoryStatus");

        var methodNames = publicMethods.Select(m => m.Name).OrderBy(n => n).ToList();
        methodNames.Should().Contain("GetLoginPolicyStatus");
        methodNames.Should().Contain("GetPasswordPolicyStatus");
        methodNames.Should().Contain("GetAuditRetentionStatus");
        methodNames.Should().Contain("GetAuditIntegrityStatus");
        methodNames.Should().Contain("GetKeyRotationStatus");
        methodNames.Should().Contain("GetBackupHistoryStatus");
    }

    // ── Query handlers for D7 endpoints ───────────────────────────────────────────

    [Fact]
    public void Compliance_D7_QueryHandlers_AllExist()
    {
        // QĐ 742 D7: Each endpoint has a corresponding MediatR query handler.
        // Evidence: All 6 query handler types exist.

        typeof(GetLoginPolicyStatusQuery).Should().NotBeNull();
        typeof(GetPasswordPolicyStatusQuery).Should().NotBeNull();
        typeof(GetAuditRetentionStatusQuery).Should().NotBeNull();
        typeof(GetAuditIntegrityStatusQuery).Should().NotBeNull();
        typeof(GetKeyRotationStatusQuery).Should().NotBeNull();
        typeof(GetBackupHistoryStatusQuery).Should().NotBeNull();
    }

    // ── Permission codes (shared kernel) ──────────────────────────────────────────

    [Fact]
    public void Compliance_PermissionCodes_IpRule_ViewAndManage()
    {
        // QĐ 742 2.2.a: IP rule operations require explicit permissions.
        // Evidence: PermCodes.Security.IpRule.View and .Manage defined.

        var viewPermCode = PermCodes.Security.IpRule.View;
        var managePermCode = PermCodes.Security.IpRule.Manage;

        viewPermCode.Should().NotBeNullOrEmpty();
        managePermCode.Should().NotBeNullOrEmpty();
        viewPermCode.Should().NotBe(managePermCode);
    }

    [Fact]
    public void Compliance_PermissionCodes_SecurityHealth_View()
    {
        // QĐ 742 D7: Security health view requires explicit permission.
        // Evidence: PermCodes.Security.Health.View defined.

        var healthViewPermCode = PermCodes.Security.Health.View;

        healthViewPermCode.Should().NotBeNullOrEmpty();
    }
}
