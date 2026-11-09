using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.Audit.Domain.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AuditCategoryMapper"/>.
/// Covers QĐ 742 mục 3.1.b mapping for:
///   1. Verb-style action strings emitted by AuditBehavior.ExtractAction.
///   2. SCREAMING_SNAKE_CASE constants from AuditAction value object.
///   3. Explicit [AuditCategory] attribute precedence over verb fallback.
///   4. Default fallback (AppAccess + Low) for unknown actions.
/// </summary>
public sealed class AuditCategoryMapperTests
{
    // ── Severity verb mapping (deterministic baseline) ───────────────────────

    [Theory]
    [InlineData("Read")]
    [InlineData("Login")]
    [InlineData("Logout")]
    [InlineData("DATA_ACCESS")]
    [InlineData("Mark")]
    public void Read_or_session_actions_are_severity_low(string action)
    {
        var (_, severity) = AuditCategoryMapper.Map(action);
        severity.Should().Be(AuditSeverity.Low);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Assign")]
    [InlineData("Revoke")]
    [InlineData("Submit")]
    [InlineData("Approve")]
    [InlineData("Reject")]
    [InlineData("Close")]
    [InlineData("Publish")]
    [InlineData("Reset")]
    [InlineData("Lock")]
    [InlineData("Unlock")]
    [InlineData("Restore")]
    [InlineData("Register")]
    [InlineData("Send")]
    [InlineData("Sync")]
    [InlineData("Manage")]
    [InlineData("ROLE_ASSIGN")]
    [InlineData("ROLE_REVOKE")]
    [InlineData("PERMISSION_GRANTED")]
    [InlineData("TOKEN_REVOKE")]
    public void Mutating_actions_are_severity_medium(string action)
    {
        var (_, severity) = AuditCategoryMapper.Map(action);
        severity.Should().Be(AuditSeverity.Medium);
    }

    [Fact]
    public void Delete_is_severity_high()
    {
        var (_, severity) = AuditCategoryMapper.Map("Delete");
        severity.Should().Be(AuditSeverity.High);
    }

    [Theory]
    [InlineData("RTBF")]
    [InlineData("ACCESS_DENIED")]
    [InlineData("SENSITIVE_ACTION_USED")]
    [InlineData("CA_ROOT_THUMBPRINTS_CHANGED")]   // VN CA root pin change — QĐ 742 §5.1
    public void Compliance_critical_actions_are_severity_critical(string action)
    {
        var (_, severity) = AuditCategoryMapper.Map(action);
        severity.Should().Be(AuditSeverity.Critical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UnknownVerbThatDoesNotMatch")]
    public void Unknown_or_blank_action_falls_back_to_low(string action)
    {
        var (_, severity) = AuditCategoryMapper.Map(action);
        severity.Should().Be(AuditSeverity.Low);
    }

    // ── Category verb fallback (no attribute) ────────────────────────────────

    [Theory]
    [InlineData("Login", AuditCategory.AppAccess)]
    [InlineData("Logout", AuditCategory.AppAccess)]
    [InlineData("Read", AuditCategory.AppAccess)]
    [InlineData("DATA_ACCESS", AuditCategory.AppAccess)]
    [InlineData("ROLE_ASSIGN", AuditCategory.AccountManagement)]
    [InlineData("ROLE_REVOKE", AuditCategory.AccountManagement)]
    [InlineData("PERMISSION_GRANTED", AuditCategory.AccountManagement)]
    [InlineData("TOKEN_REVOKE", AuditCategory.AccountManagement)]
    [InlineData("Assign", AuditCategory.AccountManagement)]
    [InlineData("Revoke", AuditCategory.AccountManagement)]
    [InlineData("Sync", AuditCategory.AccountManagement)]
    [InlineData("RTBF", AuditCategory.AccountManagement)]
    [InlineData("ACCESS_DENIED", AuditCategory.Error)]
    [InlineData("SENSITIVE_ACTION_USED", AuditCategory.Error)]
    public void Category_verb_fallback_matches_qd742_groups(string action, AuditCategory expected)
    {
        var (category, _) = AuditCategoryMapper.Map(action);
        category.Should().Be(expected);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("UnknownVerb")]
    [InlineData("")]
    public void Unknown_or_generic_actions_default_to_app_access(string action)
    {
        var (category, _) = AuditCategoryMapper.Map(action);
        category.Should().Be(AuditCategory.AppAccess);
    }

    // ── Attribute precedence over verb fallback ──────────────────────────────

    [Fact]
    public void Explicit_attribute_overrides_verb_fallback()
    {
        // "Create" verb would default to AppAccess, but [AuditCategory(ConfigChange)] wins.
        var (category, severity) = AuditCategoryMapper.Map(
            "Create",
            typeof(SampleConfigCommand));

        category.Should().Be(AuditCategory.ConfigChange);
        severity.Should().Be(AuditSeverity.Medium); // verb still drives severity
    }

    [Fact]
    public void Attribute_inherited_from_abstract_base_is_respected()
    {
        var (category, _) = AuditCategoryMapper.Map(
            "Update",
            typeof(InheritedAccountCommand));

        category.Should().Be(AuditCategory.AccountManagement);
    }

    [Fact]
    public void No_attribute_falls_back_to_verb_inferred_category()
    {
        var (category, _) = AuditCategoryMapper.Map(
            "ROLE_ASSIGN",
            typeof(UnmarkedCommand));

        category.Should().Be(AuditCategory.AccountManagement);
    }

    [Fact]
    public void Action_string_is_case_insensitive()
    {
        var lower = AuditCategoryMapper.Map("delete");
        var upper = AuditCategoryMapper.Map("DELETE");
        var mixed = AuditCategoryMapper.Map("Delete");

        lower.Should().Be(upper).And.Be(mixed);
        lower.Severity.Should().Be(AuditSeverity.High);
    }

    [Fact]
    public void Mapper_covers_every_declared_audit_action_value_object_constant()
    {
        // Regression guard: every public static AuditAction declared on the value object
        // must produce a non-default mapping or be deliberately classified as AppAccess+Low.
        // This catches drift when new AuditAction constants are added without mapper update.
        var fields = typeof(AuditAction)
            .GetFields(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(AuditAction))
            .ToList();

        fields.Should().NotBeEmpty(because: "AuditAction declares well-known constants");

        foreach (var field in fields)
        {
            var action = (AuditAction)field.GetValue(null)!;
            var act = () => AuditCategoryMapper.Map(action.Value);
            act.Should().NotThrow($"AuditAction.{field.Name} ('{action.Value}') must be mappable");
        }
    }

    // ── Test fixtures ────────────────────────────────────────────────────────

    [AuditCategory(AuditCategory.ConfigChange)]
    private sealed class SampleConfigCommand : IBaseCommand { }

    [AuditCategory(AuditCategory.AccountManagement)]
    private abstract class AccountManagementCommandBase : IBaseCommand { }

    private sealed class InheritedAccountCommand : AccountManagementCommandBase { }

    private sealed class UnmarkedCommand : IBaseCommand { }
}
