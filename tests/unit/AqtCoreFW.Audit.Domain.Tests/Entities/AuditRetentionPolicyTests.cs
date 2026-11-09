using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.Audit.Domain.Tests.Entities;

/// <summary>
/// Unit tests for <see cref="AuditRetentionPolicy"/> — T1 entity (long Id, DB-assigned PK).
/// Covers factory invariants, retention mutators, archive enable/disable lifecycle,
/// and the (tenantId, category) identity rules that drive resolution precedence at purge time.
/// </summary>
public sealed class AuditRetentionPolicyTests
{
    [Fact]
    public void Factory_creates_policy_with_required_fields()
    {
        var tenantId = Guid.NewGuid();
        var policy = AuditRetentionPolicy.Create(
            tenantId: tenantId,
            category: AuditCategory.AccountManagement,
            retentionDays: 365);

        // T1: Id is long (DB-assigned after EF save); value is 0 before insert — just assert not negative.
        policy.Id.Should().BeGreaterThanOrEqualTo(0);
        policy.TenantId.Should().Be(tenantId);
        policy.Category.Should().Be(AuditCategory.AccountManagement);
        policy.RetentionDays.Should().Be(365);
        policy.ArchiveBeforePurge.Should().BeFalse();
        policy.ArchivePath.Should().BeNull();
    }

    [Fact]
    public void Factory_accepts_global_default_with_null_tenant_and_null_category()
    {
        var policy = AuditRetentionPolicy.Create(
            tenantId: null,
            category: null,
            retentionDays: 90);

        policy.TenantId.Should().BeNull();
        policy.Category.Should().BeNull();
        policy.RetentionDays.Should().Be(90);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Factory_rejects_non_positive_retention_days(int days)
    {
        var act = () => AuditRetentionPolicy.Create(null, null, days);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("retentionDays");
    }

    [Fact]
    public void Factory_requires_archive_path_when_archive_enabled()
    {
        var act = () => AuditRetentionPolicy.Create(
            null, null, retentionDays: 30,
            archiveBeforePurge: true,
            archivePath: null);
        act.Should().Throw<ArgumentException>().WithParameterName("archivePath");
    }

    [Fact]
    public void Factory_drops_archive_path_when_archive_disabled()
    {
        var policy = AuditRetentionPolicy.Create(
            null, null, retentionDays: 30,
            archiveBeforePurge: false,
            archivePath: "C:/should-be-ignored");

        policy.ArchiveBeforePurge.Should().BeFalse();
        policy.ArchivePath.Should().BeNull();
    }

    [Fact]
    public void UpdateRetention_changes_value_and_marks_updated()
    {
        var policy = AuditRetentionPolicy.Create(null, null, 30);
        policy.UpdatedAt.Should().BeNull();

        policy.UpdateRetention(180);

        policy.RetentionDays.Should().Be(180);
        policy.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public void UpdateRetention_rejects_non_positive(int days)
    {
        var policy = AuditRetentionPolicy.Create(null, null, 30);
        var act = () => policy.UpdateRetention(days);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("retentionDays");
    }

    [Fact]
    public void EnableArchive_then_DisableArchive_round_trip()
    {
        var policy = AuditRetentionPolicy.Create(null, null, 30);

        policy.EnableArchive("//archive/audit");
        policy.ArchiveBeforePurge.Should().BeTrue();
        policy.ArchivePath.Should().Be("//archive/audit");

        policy.DisableArchive();
        policy.ArchiveBeforePurge.Should().BeFalse();
        policy.ArchivePath.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EnableArchive_rejects_blank_path(string? path)
    {
        var policy = AuditRetentionPolicy.Create(null, null, 30);
        var act = () => policy.EnableArchive(path!);
        act.Should().Throw<ArgumentException>().WithParameterName("archivePath");
    }

    [Fact]
    public void Policy_has_optimistic_concurrency_token()
    {
        var prop = typeof(AuditRetentionPolicy).GetProperty(nameof(AuditRetentionPolicy.RowVersion))!;
        prop.PropertyType.Should().Be(typeof(byte[]));
    }
}
