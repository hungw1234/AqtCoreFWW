using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.MfaPolicies;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Compliance evidence tests for QÄ 742 Phase-02 (1.3.a + 1.4.d + 1.6.b)
/// and Phase-03 (1.6.a â€” MFA policy + emergency disable + account MFA enrolment).
/// Verifies that all required entities, fields, and attributes exist as per spec.
/// These are structural verification tests (reflection-based), not behavioral tests.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class Qd742Phase02ComplianceTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public Qd742Phase02ComplianceTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public void Compliance_1_3_a_ApplicationUser_HasMustChangePasswordField()
    {
        // QÄ 742 1.3.a: Force password change on first login.
        // Evidence: ApplicationUser.MustChangePassword field exists.

        // Arrange
        var userType = typeof(ApplicationUser);

        // Act
        var property = userType.GetProperty("MustChangePassword");

        // Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(bool));
        property.CanRead.Should().BeTrue();
        property.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void Compliance_1_4_d_CredentialPolicy_HasEscalationFields()
    {
        // QÄ 742 1.4.d: Lockout escalation policy fields.
        // Evidence: CredentialPolicy has EscalationLockoutCount, EscalationWindowHours, ShowRemainingAttempts.

        // Arrange
        var policyType = typeof(CredentialPolicy);

        // Act & Assert
        var escalationCountProp = policyType.GetProperty("EscalationLockoutCount");
        escalationCountProp.Should().NotBeNull();
        escalationCountProp!.PropertyType.Should().Be(typeof(int));

        var windowHoursProp = policyType.GetProperty("EscalationWindowHours");
        windowHoursProp.Should().NotBeNull();
        windowHoursProp!.PropertyType.Should().Be(typeof(int));

        var showAttemptsProp = policyType.GetProperty("ShowRemainingAttempts");
        showAttemptsProp.Should().NotBeNull();
        showAttemptsProp!.PropertyType.Should().Be(typeof(bool));
    }

    [Fact]
    public void Compliance_1_4_d_ApplicationUser_HasAutoDisableFields()
    {
        // QÄ 742 1.4.d: Auto-disable fields for escalation tracking.
        // Evidence: ApplicationUser has ConsecutiveLockoutCount, LastLockoutAt, AutoDisabledAt.

        // Arrange
        var userType = typeof(ApplicationUser);

        // Act & Assert
        var lockoutCountProp = userType.GetProperty("ConsecutiveLockoutCount");
        lockoutCountProp.Should().NotBeNull();
        lockoutCountProp!.PropertyType.Should().Be(typeof(int));

        var lastLockoutProp = userType.GetProperty("LastLockoutAt");
        lastLockoutProp.Should().NotBeNull();
        lastLockoutProp!.PropertyType.Should().Be(typeof(DateTime?));

        var autoDisabledProp = userType.GetProperty("AutoDisabledAt");
        autoDisabledProp.Should().NotBeNull();
        autoDisabledProp!.PropertyType.Should().Be(typeof(DateTime?));
    }

    [Fact]
    public void Compliance_1_6_b_RequiresMfaAttribute_Exists()
    {
        // QÄ 742 1.6.b: MFA enforcement attribute.
        // Evidence: RequiresMfaAttribute exists and is an AuthorizeAttribute.

        // Arrange & Act
        var attributeType = typeof(RequiresMfaAttribute);

        // Assert
        attributeType.Should().NotBeNull();
        attributeType.IsClass.Should().BeTrue();

        // Check inheritance from AuthorizeAttribute (via Microsoft.AspNetCore.Authorization)
        var baseType = attributeType.BaseType;
        baseType!.Name.Should().Be("AuthorizeAttribute");
    }

    [Fact]
    public void Compliance_1_6_b_MfaRequirement_Exists()
    {
        // QÄ 742 1.6.b: MFA requirement for authorization policy.
        // Evidence: MfaRequirement type exists in Authorization namespace.

        // Arrange
        var infrastructureAssembly = typeof(MfaRequirement).Assembly;

        // Act
        var requirementType = infrastructureAssembly.GetType(
            "AqtCoreFW.Identity.Infrastructure.Authorization.MfaRequirement");

        // Assert
        requirementType.Should().NotBeNull();
        requirementType!.IsClass.Should().BeTrue();

        // Check for MaxAgeHours property
        var property = requirementType.GetProperty("MaxAgeHours");
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void Compliance_1_6_b_MfaRequirementHandler_Exists()
    {
        // QÄ 742 1.6.b: MFA authorization handler.
        // Evidence: MfaRequirementHandler exists and implements AuthorizationHandler.

        // Arrange
        var infrastructureAssembly = typeof(MfaRequirementHandler).Assembly;

        // Act
        var handlerType = infrastructureAssembly.GetType(
            "AqtCoreFW.Identity.Infrastructure.Authorization.MfaRequirementHandler");

        // Assert
        handlerType.Should().NotBeNull();
        handlerType!.IsClass.Should().BeTrue();

        // Check that it's derived from AuthorizationHandler<MfaRequirement>
        var baseTypes = handlerType.BaseType?.GetGenericArguments();
        baseTypes.Should().NotBeNull();
        baseTypes![0].Name.Should().Be("MfaRequirement");
    }

    [Fact]
    public void Compliance_FirstLoginPasswordChangeMiddleware_Exists()
    {
        // QÄ 742 1.3.a: Middleware that enforces password change on first login.
        // Evidence: FirstLoginPasswordChangeMiddleware type exists.

        // Arrange & Act â€” Find via namespace scan instead of string-based GetType
        var infrastructureAssembly = typeof(MfaRequirement).Assembly;
        var middlewareType = infrastructureAssembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "FirstLoginPasswordChangeMiddleware");

        // Assert
        middlewareType.Should().NotBeNull();
        middlewareType!.IsClass.Should().BeTrue();

        // Check for InvokeAsync method (standard middleware pattern)
        var invokeMethod = middlewareType.GetMethod("InvokeAsync");
        invokeMethod.Should().NotBeNull();
    }

    [Fact]
    public void Compliance_LockoutEscalationService_Exists()
    {
        // QÄ 742 1.4.d: Service for tracking and escalating lockouts.
        // Evidence: LockoutEscalationService exists with OnLockoutAsync method.

        // Arrange & Act â€” Find via namespace scan instead of string-based GetType
        var infrastructureAssembly = typeof(MfaRequirement).Assembly;
        var serviceType = infrastructureAssembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "LockoutEscalationService");

        // Assert
        serviceType.Should().NotBeNull();
        serviceType!.IsClass.Should().BeTrue();

        // Check for OnLockoutAsync method
        var onLockoutMethod = serviceType.GetMethod("OnLockoutAsync");
        onLockoutMethod.Should().NotBeNull();
        onLockoutMethod!.ReturnType.Name.Should().Be("Task`1");

        // Check for ResetLockoutCounterAsync method
        var resetMethod = serviceType.GetMethod("ResetLockoutCounterAsync");
        resetMethod.Should().NotBeNull();
    }

    [Fact]
    public void Compliance_MfaPolicyProvider_Exists()
    {
        // QÄ 742 1.6.b: Dynamic policy provider for MFA policies.
        // Evidence: MfaPolicyProvider exists and implements IAuthorizationPolicyProvider.

        // Arrange
        var infrastructureAssembly = typeof(MfaPolicyProvider).Assembly;

        // Act
        var providerType = infrastructureAssembly.GetType(
            "AqtCoreFW.Identity.Infrastructure.Authorization.MfaPolicyProvider");

        // Assert
        providerType.Should().NotBeNull();
        providerType!.IsClass.Should().BeTrue();

        // Check for GetPolicyAsync method
        var getPolicyMethod = providerType.GetMethod("GetPolicyAsync");
        getPolicyMethod.Should().NotBeNull();
    }

    // â”€â”€ Phase-03: 1.6.a compliance evidence â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compliance_1_6_a_MfaPolicy_EntityExists()
    {
        // QÄ 742 1.6.a: Per-role MFA policy entity.
        // Evidence: MfaPolicy entity has Role, IsMandatory, GraceDays, AllowedFactors fields.

        var entityType = typeof(MfaPolicy);

        entityType.GetProperty("Role").Should().NotBeNull("MfaPolicy.Role required by 1.6.a");
        entityType.GetProperty("IsMandatory").Should().NotBeNull("MfaPolicy.IsMandatory required by 1.6.a");
        entityType.GetProperty("GraceDays").Should().NotBeNull("MfaPolicy.GraceDays required by 1.6.a (NC-5 grace 7d)");
        entityType.GetProperty("AllowedFactors").Should().NotBeNull("MfaPolicy.AllowedFactors required by 1.6.a");
    }

    [Fact]
    public void Compliance_1_6_a_MfaPolicyDto_HasAllFields()
    {
        // QÄ 742 1.6.a: MfaPolicyDto read model returned by GET admin endpoint.
        // Evidence: DTO has all required fields for admin UI.

        var dtoType = typeof(MfaPolicyDto);

        dtoType.GetProperty("Id").Should().NotBeNull();
        dtoType.GetProperty("Role").Should().NotBeNull();
        dtoType.GetProperty("IsMandatory").Should().NotBeNull();
        dtoType.GetProperty("GraceDays").Should().NotBeNull();
        dtoType.GetProperty("AllowedFactors").Should().NotBeNull();
        dtoType.GetProperty("CreatedAt").Should().NotBeNull();
    }

    [Fact]
    public void Compliance_1_6_a_ApplicationUser_HasMfaFields()
    {
        // QÄ 742 1.6.a: ApplicationUser must have TOTP enrolment + recovery codes fields.
        // Evidence: Phase-03 adds TotpSecret, TotpEnrolledAt, RecoveryCodesHash, MfaGraceUntil.

        var userType = typeof(ApplicationUser);

        userType.GetProperty("TotpSecret").Should().NotBeNull("TotpSecret required for TOTP storage");
        userType.GetProperty("TotpEnrolledAt").Should().NotBeNull("TotpEnrolledAt required to track enrolment time");
        userType.GetProperty("RecoveryCodesHash").Should().NotBeNull("RecoveryCodesHash required for BCrypt-hashed recovery codes");
        userType.GetProperty("MfaGraceUntil").Should().NotBeNull("MfaGraceUntil required for grace period tracking (NC-5)");
    }

    [Fact]
    public void Compliance_1_6_a_MfaEmergencyDisableRequest_EntityExists()
    {
        // QÄ 742 1.6.a: 2-admin approval flow for emergency MFA disable (P1-1 safeguard).
        // Evidence: MfaEmergencyDisableRequest entity with all required fields.

        var entityType = typeof(MfaEmergencyDisableRequest);

        entityType.GetProperty("TargetUserId").Should().NotBeNull("TargetUserId identifies whose MFA to disable");
        entityType.GetProperty("RequestedBy").Should().NotBeNull("RequestedBy tracks 1st admin requester");
        entityType.GetProperty("Reason").Should().NotBeNull("Reason is mandatory audit evidence");
        entityType.GetProperty("Status").Should().NotBeNull("Status tracks request lifecycle");
        entityType.GetProperty("ExpiresAt").Should().NotBeNull("ExpiresAt enforces 24h window");
    }

    [Fact]
    public void Compliance_1_6_a_MfaFactor_EnumHasTotpAndEmailOtp()
    {
        // QÄ 742 1.6.a: Allowed MFA factors include TOTP (authenticator app) and EmailOTP.
        // Evidence: MfaFactor flags enum has Totp=1 and EmailOtp=2.

        var enumType = typeof(MfaFactor);
        enumType.IsEnum.Should().BeTrue();

        var totp = (int)System.Enum.Parse(enumType, "Totp");
        totp.Should().Be(1, "Totp flag must be 1 (bit 0)");

        var emailOtp = (int)System.Enum.Parse(enumType, "EmailOtp");
        emailOtp.Should().Be(2, "EmailOtp flag must be 2 (bit 1)");
    }

    [Fact]
    public void Compliance_AccountAutoDisabledIntegrationEvent_Exists()
    {
        // QÄ 742 1.4.d: Integration event published when auto-disable is triggered.
        // Evidence: AccountAutoDisabledIntegrationEvent exists in SharedKernel.Contracts.
        // Use direct typeof â€” test project now references SharedKernel.Contracts explicitly.

        var eventType = typeof(AccountAutoDisabledIntegrationEvent);

        eventType.IsClass.Should().BeTrue();

        var userIdProp = eventType.GetProperty("UserId");
        userIdProp.Should().NotBeNull();

        var autoDisabledAtProp = eventType.GetProperty("AutoDisabledAtUtc");
        autoDisabledAtProp.Should().NotBeNull();
    }
}
