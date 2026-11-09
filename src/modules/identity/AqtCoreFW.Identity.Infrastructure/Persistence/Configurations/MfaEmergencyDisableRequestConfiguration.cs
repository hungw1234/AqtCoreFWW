using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for MfaEmergencyDisableRequest — identity schema.
/// No FK to AspNetUsers to avoid cascade complications; TargetUserId is a logical reference.
/// Index on (TargetUserId, Status) for pending-request lookups.
/// </summary>
public sealed class MfaEmergencyDisableRequestConfiguration
    : EntityTypeConfigurationBase<MfaEmergencyDisableRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<MfaEmergencyDisableRequest> builder)
    {
        builder.Property(r => r.TargetUserId).IsRequired();
        builder.Property(r => r.RequestedBy).IsRequired();

        builder.Property(r => r.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.ActionedBy);
        builder.Property(r => r.ActionedAt);
        builder.Property(r => r.ExpiresAt).IsRequired();

        // Composite index for fast pending-request lookup per target user.
        builder.HasIndex(r => new { r.TargetUserId, r.Status })
            .HasDatabaseName("IX_MfaEmergencyDisableRequests_TargetUserId_Status");
    }
}
