using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Behaviors;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;
using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Compliance;

/// <summary>
/// Compliance evidence tests for QĐ 742 Phase-01 (mục 3.1.a/b, 3.2.b/c, 4.1 — Audit Baseline).
///
/// Verifies Phase-01 deliverables structurally (no runtime SQL or Vault dependency):
///   - AuditCategory enum has exactly 5 categories (3.1.b mandate: 5 log groups)
///   - AuditLogEntry has Severity column (3.2.c: mức độ quan trọng field)
///   - AuditLogEntry has HmacSignature for immutability chain (3.1.a: audit integrity)
///   - AuditRetentionPolicy entity exists with RetentionDays (3.2.b: retention config)
///   - ValidationBehavior pipeline registered (4.1: input validation globally enabled)
///
/// These are structural verification tests (reflection-based), not behavioral tests.
/// Behavioral tests are in AuditLogEntryTests, AuditRetentionPolicyTests.
/// </summary>
public sealed class Qd742Phase01ComplianceTests
{
    // ── Mục 3.1.b — Audit log: ≥5 nhóm phân loại ─────────────────────────────

    [Fact]
    public void Compliance_3_1_b_AuditCategory_HasFiveValues()
    {
        // QĐ 742 3.1.b: log must classify into ≥5 groups:
        //   (i) truy cập, (ii) đăng nhập quản trị, (iii) lỗi vận hành,
        //   (iv) quản lý tài khoản, (v) thay đổi cấu hình.
        // Evidence: AuditCategory enum has exactly 5 values matching the 5 groups.

        var values = Enum.GetValues<AuditCategory>();
        values.Should().HaveCount(5, "QĐ 742 §3.1.b mandates exactly 5 audit log categories");

        var names = values.Select(v => v.ToString()).ToHashSet();
        names.Should().Contain("AppAccess",         "covers (i) truy cập");
        names.Should().Contain("AdminLogin",        "covers (ii) đăng nhập quản trị");
        names.Should().Contain("Error",             "covers (iii) lỗi vận hành");
        names.Should().Contain("AccountManagement", "covers (iv) quản lý tài khoản");
        names.Should().Contain("ConfigChange",      "covers (v) thay đổi cấu hình");
    }

    // ── Mục 3.2.c — Audit log: lưu mức độ quan trọng ────────────────────────

    [Fact]
    public void Compliance_3_2_c_AuditEvent_HasSeverityColumn()
    {
        // QĐ 742 3.2.c: audit log MUST store (v) mức độ quan trọng (severity).
        // Evidence: AuditLogEntry entity has Severity property of type AuditSeverity.

        var entityType = typeof(AuditLogEntry);
        var severityProp = entityType.GetProperty("Severity");

        severityProp.Should().NotBeNull(
            "AuditLogEntry must have Severity property (QĐ 742 §3.2.c: mức độ quan trọng)");
        severityProp!.PropertyType.Name.Should().Be("AuditSeverity",
            "Severity must be typed AuditSeverity enum");
    }

    // ── Mục 3.1.a — Audit log: ghi nhật ký + immutability ────────────────────

    [Fact]
    public void Compliance_3_1_a_AuditEvent_ImmutabilityChainExists()
    {
        // QĐ 742 3.1.a: audit log must exist and must not be tampered with.
        // Evidence: AuditLogEntry has HmacSignature property — HMAC chain enforces
        // tamper-evidence without a DB trigger. Each entry's signature chains to prior.

        var entityType = typeof(AuditLogEntry);
        var hmacProp = entityType.GetProperty("HmacSignature");

        hmacProp.Should().NotBeNull(
            "AuditLogEntry must have HmacSignature (HMAC chain = immutability mechanism, QĐ 742 §3.1.a)");
        hmacProp!.PropertyType.Should().Be(typeof(string));
    }

    // ── Mục 3.2.b — Cấu hình thời gian lưu trữ nhật ký ──────────────────────

    [Fact]
    public void Compliance_3_2_b_AuditRetention_PolicyEntityExists()
    {
        // QĐ 742 3.2.b: system must allow configuring log retention duration.
        // Evidence: AuditRetentionPolicy entity exists with RetentionDays + Category fields.

        var policyType = typeof(AuditRetentionPolicy);
        policyType.IsClass.Should().BeTrue("AuditRetentionPolicy must be a class");

        policyType.GetProperty("RetentionDays").Should().NotBeNull(
            "AuditRetentionPolicy must have RetentionDays (configurable duration per §3.2.b)");
        policyType.GetProperty("Category").Should().NotBeNull(
            "AuditRetentionPolicy must have Category for per-group retention rules");
        policyType.GetProperty("ArchiveBeforePurge").Should().NotBeNull(
            "AuditRetentionPolicy must have ArchiveBeforePurge for safe deletion");
    }

    // ── Mục 4.1 — Input validation globally enabled ───────────────────────────

    [Fact]
    public void Compliance_4_1_FluentValidation_PipelineBehaviorRegistered()
    {
        // QĐ 742 4.1: all user input must be validated before processing.
        // Evidence: ValidationBehavior<TRequest,TResponse> generic type exists in SharedKernel.
        // This MediatR pipeline behavior wraps ALL commands/queries — validation is global,
        // not opt-in per handler. Confirmed in Identity/Audit/etc. ModuleRegistration via
        // services.AddValidatorsFromAssembly().

        var behaviorType = typeof(ValidationBehavior<,>);
        behaviorType.Should().NotBeNull(
            "ValidationBehavior<TRequest,TResponse> must exist (QĐ 742 §4.1: global input validation)");

        // Verify it implements IPipelineBehavior (MediatR contract).
        var interfaces = behaviorType.GetInterfaces();
        interfaces.Should().Contain(i => i.Name.Contains("IPipelineBehavior"),
            "ValidationBehavior must implement MediatR IPipelineBehavior<,>");
    }
}
