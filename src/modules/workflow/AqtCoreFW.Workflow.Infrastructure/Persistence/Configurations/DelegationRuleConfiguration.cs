using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for DelegationRule.
/// Base class auto-adds: IX_DelegationRule_IsDeleted, IX_DelegationRule_TenantId_Id,
/// IX_DelegationRule_TenantId_CreatedAt (descending) — all via EntityTypeConfigurationBase.
/// Custom index: (TenantId, DelegatorUserId, IsActive) for active-rule lookup per user.
/// ValidFrom/Until stored as datetimeoffset(7) — preserves timezone for cross-timezone GOV usage.
/// </summary>
public sealed class DelegationRuleConfiguration
    : EntityTypeConfigurationBase<DelegationRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DelegationRule> builder)
    {
        builder.ToTable("DelegationRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.DelegatorUserId).IsRequired();
        builder.Property(r => r.DelegateUserId).IsRequired();
        builder.Property(r => r.ValidFrom).IsRequired();
        builder.Property(r => r.ValidUntil).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.TenantId).IsRequired();

        // Active-rule lookup: "does user X have an active delegation rule in tenant T?"
        builder.HasIndex(r => new { r.TenantId, r.DelegatorUserId, r.IsActive })
            .HasDatabaseName("IX_DelegationRules_TenantId_DelegatorUserId_IsActive");

        // Expiry job: find rules past ValidUntil to deactivate
        builder.HasIndex(r => new { r.TenantId, r.ValidUntil })
            .HasDatabaseName("IX_DelegationRules_TenantId_ValidUntil");
    }
}
