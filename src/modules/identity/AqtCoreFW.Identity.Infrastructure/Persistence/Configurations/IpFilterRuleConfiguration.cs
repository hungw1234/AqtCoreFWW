using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for IpFilterRule (T1, [identity] schema).
///
/// Indexes:
///   IX_IpFilterRule_TenantId_IsEnabled — primary query pattern (load rules for middleware).
///   IX_IpFilterRule_Cidr               — CIDR lookup / duplicate-check.
///
/// Memory: SQL Server schema [identity] is a reserved keyword — bracket-escaped via ToTable().
/// Base class applies: bigint IDENTITY PK, datetime2(3) timestamps, soft-delete filter+index,
/// PageCompression annotation, and standard audit columns.
/// </summary>
public sealed class IpFilterRuleConfiguration : EntityTypeConfigurationBase<IpFilterRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<IpFilterRule> builder)
    {
        builder.Property(r => r.Cidr)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasMaxLength(500)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        // DB DEFAULT 1 collides with the CLR default (false): without a sentinel, EF treats a
        // false value as "unset" and omits the column on INSERT, letting the DB default flip the
        // rule to enabled/block. HasSentinel(true) marks true as the unset marker so an explicit
        // false is always written — preserving disabled rules and allow (whitelist) rules.
        builder.Property(r => r.IsEnabled)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(true);

        builder.Property(r => r.IsBlock)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(true);

        // TenantId nullable — null = global rule.
        builder.Property(r => r.TenantId)
            .IsRequired(false);

        // Primary access pattern: load enabled rules (all tenants) for middleware.
        builder.HasIndex(r => new { r.TenantId, r.IsEnabled })
            .HasDatabaseName("IX_IpFilterRule_TenantId_IsEnabled");

        // CIDR uniqueness: one rule per (TenantId, Cidr) pair among non-deleted rows.
        // UQ_IpFilterRule_TenantId_Cidr enforces DB-level dedup.
        // SQL Server allows multiple NULLs in unique indexes; NULL TenantId (global rules)
        // share the same uniqueness space — multiple global rules with the same CIDR are rejected.
        // The HasFilter IS NULL branch handles global rules at DB level.
        builder.HasIndex(r => new { r.TenantId, r.Cidr })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UQ_IpFilterRule_TenantId_Cidr");
    }
}
