using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class CredentialPolicyConfiguration : EntityTypeConfigurationBase<CredentialPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CredentialPolicy> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();

        // QĐ 742 1.4.d/b: escalation + remaining-attempts fields with DB defaults
        builder.Property(p => p.EscalationLockoutCount)
            .IsRequired()
            .HasDefaultValueSql("3");
        builder.Property(p => p.EscalationWindowHours)
            .IsRequired()
            .HasDefaultValueSql("24");
        // Sentinel `true` is required because DB default (1=true) ≠ CLR default for bool (false).
        // Without it, EF treats CLR-false as "use DB default" → silently flips false → true on INSERT.
        // With Sentinel(true): explicit-false → emit 0 (false); explicit-true → omit → DB default 1 (true).
        builder.Property(p => p.ShowRemainingAttempts)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(true);

        // Only one default policy allowed per tenant (partial unique index)
        // Note: base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped — manual TenantId index dropped.
        builder.HasIndex(p => new { p.TenantId, p.IsDefault });
    }
}
