using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Domain.Tests.Entities;

/// <summary>
/// Unit tests for the QĐ 742 Slice B fields on <see cref="AuditLogEntry"/>:
/// Category, Severity, SchemaVersion. Existing factory call sites (3 callers) must
/// remain BC — Category/Severity defaults match the verb-fallback used by the mapper.
/// </summary>
public sealed class AuditLogEntryCategorySeverityTests
{
    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    [Fact]
    public void Default_factory_call_sets_app_access_low_schema_v2()
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            tenantId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            userName: "u",
            action: "Create",
            moduleName: "Audit",
            resourceType: "Sample",
            resourceId: null,
            dataSnapshot: null,
            ipAddress: null,
            correlationId: null);

        entry.Category.Should().Be(AuditCategory.AppAccess);
        entry.Severity.Should().Be(AuditSeverity.Low);
        entry.SchemaVersion.Should().Be(2);
    }

    [Theory]
    [InlineData(AuditCategory.AccountManagement, AuditSeverity.Medium)]
    [InlineData(AuditCategory.ConfigChange, AuditSeverity.High)]
    [InlineData(AuditCategory.AdminLogin, AuditSeverity.Critical)]
    [InlineData(AuditCategory.Error, AuditSeverity.Low)]
    public void Factory_accepts_explicit_category_and_severity(
        AuditCategory category, AuditSeverity severity)
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            tenantId: null,
            userId: null,
            userName: "system",
            action: "Update",
            moduleName: "Identity",
            resourceType: "User",
            resourceId: "abc",
            dataSnapshot: null,
            ipAddress: null,
            correlationId: null,
            category: category,
            severity: severity);

        entry.Category.Should().Be(category);
        entry.Severity.Should().Be(severity);
    }

    [Fact]
    public void Factory_accepts_explicit_schema_version_for_legacy_replay()
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            tenantId: null,
            userId: null,
            userName: "system",
            action: "Login",
            moduleName: "Identity",
            resourceType: "Session",
            resourceId: null,
            dataSnapshot: null,
            ipAddress: null,
            correlationId: null,
            schemaVersion: 1);

        entry.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public void Existing_callers_unchanged_remain_backward_compatible()
    {
        // Mirrors the call signature used by AuditService and LogAuditEventCommandHandler today —
        // their argument list is positional and must not break when new optional params are added.
        var entry = AuditLogEntry.Create(
            NewGen(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user@example.com",
            "Login",
            "Identity",
            "Session",
            null,
            null,
            "127.0.0.1",
            "trace-1");

        entry.Action.Should().Be("Login");
        entry.IpAddress.Should().Be("127.0.0.1");
        entry.CorrelationId.Should().Be("trace-1");
        entry.HmacSignature.Should().BeEmpty(); // populated later by HmacAuditService
    }

    [Fact]
    public void Category_and_severity_are_private_set_through_factory_only()
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            null, null, "u", "Read", "M", "R", null, null, null, null,
            category: AuditCategory.Error,
            severity: AuditSeverity.Critical);

        // Reflection check — only the factory mutates these; no public setter.
        var categoryProp = typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.Category))!;
        var severityProp = typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.Severity))!;

        categoryProp.SetMethod!.IsPrivate.Should().BeTrue();
        severityProp.SetMethod!.IsPrivate.Should().BeTrue();

        entry.Category.Should().Be(AuditCategory.Error);
        entry.Severity.Should().Be(AuditSeverity.Critical);
    }
}
