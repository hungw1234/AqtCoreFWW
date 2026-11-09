using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Compliance;

/// <summary>
/// Compliance evidence tests for QĐ 742 Phase-03 (mục 1.6.a/b — MFA Policy).
///
/// Verifies Phase-03 deliverables structurally (no runtime SQL or Vault dependency):
///   - MfaPolicy entity exists with required fields (1.6.a: quản lý chính sách MFA)
///   - RecoveryCodeService uses BCrypt + generates exactly 16 codes (1.6.b security hygiene)
///   - RequiresMfaAttribute type exists in SharedKernel (1.6.b: policy enforcement contract)
///   - MfaPolicyProvider resolves RequiresMfa:{hours} policies (1.6.b: dynamic enforcement)
///   - MfaRequirementHandler validates mfa_at claim (1.6.b: token-level MFA enforcement)
///
/// These are structural verification tests (reflection-based), not behavioral tests.
/// Behavioral tests are in MfaPolicyTests, RecoveryCodeServiceTests, MfaPolicyProviderTests.
/// </summary>
public sealed class Qd742Phase03ComplianceTests
{
    // ── Mục 1.6.a — Giao diện quản lý chính sách MFA ─────────────────────────

    [Fact]
    public void Compliance_1_6_a_MfaPolicy_EntityExists()
    {
        // QĐ 742 1.6.a: system must have a manageable MFA policy entity.
        // Evidence: MfaPolicy class exists in Identity.Domain with required fields.

        var policyType = typeof(MfaPolicy);
        policyType.IsClass.Should().BeTrue("MfaPolicy must be a class");

        // Role-scoped policy (per-role MFA mandate).
        policyType.GetProperty("Role").Should().NotBeNull(
            "MfaPolicy must have Role property for per-role scoping");

        // Core policy flags.
        policyType.GetProperty("IsMandatory").Should().NotBeNull(
            "MfaPolicy must have IsMandatory flag");
        policyType.GetProperty("GraceDays").Should().NotBeNull(
            "MfaPolicy must have GraceDays for grace period before enforcement");
        policyType.GetProperty("AllowedFactors").Should().NotBeNull(
            "MfaPolicy must have AllowedFactors bitmask (TOTP, recovery code, etc.)");

        // Public identity (T2 entity — addressed by PublicId guid).
        policyType.GetProperty("PublicId").Should().NotBeNull(
            "MfaPolicy must have PublicId (IPublicIdentified contract for T2 addressing)");
    }

    // ── Mục 1.6.b — RecoveryCodeService security parameters ─────────────────

    [Fact]
    public void Compliance_1_6_b_RecoveryCodeService_Generates16CodesWithBCrypt()
    {
        // QĐ 742 1.6.b: MFA recovery codes must be:
        //   - Sufficient quantity (16 codes per NIST SP 800-63B guidance)
        //   - BCrypt-hashed at storage (not stored plaintext)
        // Evidence: RecoveryCodeService private constants CodeCount=16, BcryptWorkFactor=11.

        var serviceType = typeof(RecoveryCodeService);
        serviceType.IsClass.Should().BeTrue("RecoveryCodeService must be a class");

        // CodeCount = 16 (private const — requires NonPublic flag).
        var codeCountField = serviceType.GetField(
            "CodeCount",
            BindingFlags.NonPublic | BindingFlags.Static);
        codeCountField.Should().NotBeNull(
            "RecoveryCodeService must have private const CodeCount");
        ((int)codeCountField!.GetValue(null)!).Should().Be(16,
            "RecoveryCodeService must generate exactly 16 recovery codes per NIST SP 800-63B");

        // BcryptWorkFactor = 11 (private const — prevents brute-force on stolen hash file).
        var bcryptField = serviceType.GetField(
            "BcryptWorkFactor",
            BindingFlags.NonPublic | BindingFlags.Static);
        bcryptField.Should().NotBeNull(
            "RecoveryCodeService must have private const BcryptWorkFactor");
        ((int)bcryptField!.GetValue(null)!).Should().BeGreaterThanOrEqualTo(10,
            "BCrypt work factor must be ≥10 for adequate brute-force resistance");
    }

    // ── Mục 1.6.b — RequiresMfa enforcement contract ─────────────────────────

    [Fact]
    public void Compliance_1_6_b_RequiresMfaAttribute_ExistsInSharedKernel()
    {
        // QĐ 742 1.6.b: MFA enforcement must be applied uniformly across admin controllers.
        // Evidence: RequiresMfaAttribute exists in SharedKernel.Authorization —
        // shared contract ensures all modules can enforce MFA without duplicating logic.

        var attrType = typeof(RequiresMfaAttribute);
        attrType.IsClass.Should().BeTrue("RequiresMfaAttribute must be a class");
        attrType.IsSealed.Should().BeTrue("RequiresMfaAttribute must be sealed (no bypass via subclass)");

        // PolicyPrefix public const drives dynamic MfaPolicyProvider resolution.
        var prefixField = attrType.GetField(
            "PolicyPrefix",
            BindingFlags.Public | BindingFlags.Static);
        prefixField.Should().NotBeNull("RequiresMfaAttribute must expose PolicyPrefix public const");
        ((string)prefixField!.GetValue(null)!).Should().StartWith("RequiresMfa:",
            "PolicyPrefix must start with 'RequiresMfa:' for MfaPolicyProvider routing");
    }

    // ── Mục 1.6.b — MfaPolicyProvider dynamic enforcement ────────────────────

    [Fact]
    public void Compliance_1_6_b_MfaPolicyProvider_ExistsAndResolvesRequiresMfaPrefix()
    {
        // QĐ 742 1.6.b: MFA policy must be enforced dynamically per-controller/action.
        // Evidence: MfaPolicyProvider resolves RequiresMfa:{hours} authorization policies
        // at runtime — no up-front registration needed, enforcement scales across all routes.

        var providerType = typeof(MfaPolicyProvider);
        providerType.IsClass.Should().BeTrue("MfaPolicyProvider must be a class");
        providerType.IsSealed.Should().BeTrue("MfaPolicyProvider must be sealed");

        // Must implement IAuthorizationPolicyProvider (ASP.NET Core contract).
        var interfaces = providerType.GetInterfaces();
        interfaces.Should().Contain(i => i.Name.Contains("IAuthorizationPolicyProvider"),
            "MfaPolicyProvider must implement IAuthorizationPolicyProvider");

        // GetPolicyAsync is the resolution hook.
        var method = providerType.GetMethod("GetPolicyAsync");
        method.Should().NotBeNull("MfaPolicyProvider must override GetPolicyAsync");
    }

    // ── Mục 1.6.b — mfa_at claim token enforcement ────────────────────────────

    [Fact]
    public void Compliance_1_6_b_MfaRequirementHandler_ValidatesMfaAtClaim()
    {
        // QĐ 742 1.6.b: MFA step-up must be verified at token level, not just at login.
        // Evidence: MfaRequirementHandler rejects requests whose MFA verification is older
        // than MaxAgeHours. The mfa_at claim-type constant and the recency check live in the
        // shared MfaClaimEvaluator so the handler and MfaForbidResultHandler derive the exact
        // same verdict (the handler delegates its recency decision to that evaluator).

        var handlerType = typeof(MfaRequirementHandler);
        handlerType.IsClass.Should().BeTrue("MfaRequirementHandler must be a class");
        handlerType.IsSealed.Should().BeTrue("MfaRequirementHandler must be sealed");

        // mfa_at claim type constant — owned by the shared evaluator (public const).
        var claimTypeField = typeof(MfaClaimEvaluator).GetField(
            nameof(MfaClaimEvaluator.MfaAtClaimType),
            BindingFlags.Public | BindingFlags.Static);
        claimTypeField.Should().NotBeNull(
            "MfaClaimEvaluator must expose the MfaAtClaimType constant");
        ((string)claimTypeField!.GetValue(null)!).Should().Be("mfa_at",
            "MfaAtClaimType must be 'mfa_at' — the claim stamped at MFA verification time");
    }
}
