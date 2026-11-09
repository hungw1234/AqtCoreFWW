using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Jobs;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Audit.Infrastructure.Tests.Jobs;

/// <summary>
/// Pure-function tests for <see cref="AuditLogPurgeJob.ResolvePolicy"/>.
/// Asserts the precedence ladder declared in the plan §"AuditRetentionPolicy":
///   (tenant + category) → (tenant + null cat) → (null tenant + null cat) → null.
/// No DB needed — these guard against silent precedence drift between the in-memory
/// resolver and <see cref="Domain.Repositories.IAuditRetentionPolicyRepository.ResolveAsync"/>.
/// </summary>
public sealed class AuditLogPurgeJobResolveTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public void ResolvePolicy_TenantAndCategoryMatch_BeatsTenantOnly()
    {
        var policies = new List<AuditRetentionPolicy>
        {
            AuditRetentionPolicy.Create(TenantA, AuditCategory.AppAccess, 30),
            AuditRetentionPolicy.Create(TenantA, null, 60),
            AuditRetentionPolicy.Create(null, null, 365),
        };

        var resolved = AuditLogPurgeJob.ResolvePolicy(policies, TenantA, AuditCategory.AppAccess);
        resolved.Should().NotBeNull();
        resolved!.RetentionDays.Should().Be(30);
    }

    [Fact]
    public void ResolvePolicy_TenantOnlyMatch_BeatsGlobal()
    {
        var policies = new List<AuditRetentionPolicy>
        {
            AuditRetentionPolicy.Create(TenantA, null, 60),
            AuditRetentionPolicy.Create(null, null, 365),
        };

        var resolved = AuditLogPurgeJob.ResolvePolicy(policies, TenantA, AuditCategory.Error);
        resolved.Should().NotBeNull();
        resolved!.RetentionDays.Should().Be(60);
    }

    [Fact]
    public void ResolvePolicy_GlobalOnly_FallbackWhenTenantUnknown()
    {
        var policies = new List<AuditRetentionPolicy>
        {
            AuditRetentionPolicy.Create(TenantB, AuditCategory.AppAccess, 30),
            AuditRetentionPolicy.Create(null, null, 365),
        };

        var resolved = AuditLogPurgeJob.ResolvePolicy(policies, TenantA, AuditCategory.AppAccess);
        resolved.Should().NotBeNull();
        resolved!.RetentionDays.Should().Be(365);
    }

    [Fact]
    public void ResolvePolicy_NoMatch_ReturnsNull()
    {
        var policies = new List<AuditRetentionPolicy>
        {
            AuditRetentionPolicy.Create(TenantB, AuditCategory.AppAccess, 30),
        };

        var resolved = AuditLogPurgeJob.ResolvePolicy(policies, TenantA, AuditCategory.AppAccess);
        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolvePolicy_GlobalCategoryMatch_BeatsGlobalDefault()
    {
        var policies = new List<AuditRetentionPolicy>
        {
            AuditRetentionPolicy.Create(null, AuditCategory.AdminLogin, 90),
            AuditRetentionPolicy.Create(null, null, 365),
        };

        // No tenant-specific policy; the most-specific global wins (category-scoped > unscoped).
        var resolved = AuditLogPurgeJob.ResolvePolicy(policies, TenantA, AuditCategory.AdminLogin);
        resolved.Should().NotBeNull();
        resolved!.RetentionDays.Should().Be(90);
    }

    [Fact]
    public void ResolvePolicy_LogRowWithNullTenant_FallsBackToGlobal()
    {
        var policies = new List<AuditRetentionPolicy>
        {
            AuditRetentionPolicy.Create(null, null, 365),
        };

        var resolved = AuditLogPurgeJob.ResolvePolicy(policies, tenantId: null, AuditCategory.AppAccess);
        resolved.Should().NotBeNull();
        resolved!.RetentionDays.Should().Be(365);
    }
}
