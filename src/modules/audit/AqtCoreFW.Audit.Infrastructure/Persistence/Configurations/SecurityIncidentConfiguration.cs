using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

public sealed class SecurityIncidentConfiguration : IEntityTypeConfiguration<SecurityIncident>
{
    public void Configure(EntityTypeBuilder<SecurityIncident> b)
    {
        b.ToTable("SecurityIncidents");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.Title).HasMaxLength(512).IsRequired();
        b.Property(e => e.Description).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(e => e.Mitigations).HasColumnType("nvarchar(max)");
        b.Property(e => e.Severity).HasConversion<int>();
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);

        b.HasIndex(e => new { e.TenantId, e.Status });
        b.HasIndex(e => e.OccurredAt);
    }
}
