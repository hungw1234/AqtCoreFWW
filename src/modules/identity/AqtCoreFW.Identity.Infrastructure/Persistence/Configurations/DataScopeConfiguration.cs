using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class DataScopeTypeConfiguration : IEntityTypeConfiguration<DataScopeType>
{
    public void Configure(EntityTypeBuilder<DataScopeType> builder)
    {
        builder.ToTable("DataScopeTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        // Code must be unique across the identity schema
        builder.HasIndex(t => t.Code).IsUnique();
    }
}

public sealed class RoleDataScopeConfiguration : IEntityTypeConfiguration<RoleDataScope>
{
    public void Configure(EntityTypeBuilder<RoleDataScope> builder)
    {
        builder.ToTable("RoleDataScopes");
        builder.HasKey(rds => rds.Id);
        builder.Property(rds => rds.ScopeField).HasMaxLength(100);
        builder.Property(rds => rds.ScopeValue).HasMaxLength(500);
        builder.HasIndex(rds => rds.RoleId);
        builder.HasIndex(rds => new { rds.RoleId, rds.DataScopeTypeId });

        builder.HasOne(rds => rds.Role)
            .WithMany()
            .HasForeignKey(rds => rds.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rds => rds.DataScopeType)
            .WithMany()
            .HasForeignKey(rds => rds.DataScopeTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserDataScopeOverrideConfiguration : EntityTypeConfigurationBase<UserDataScopeOverride>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserDataScopeOverride> builder)
    {
        builder.ToTable("UserDataScopeOverrides");
        builder.Property(o => o.Reason).HasMaxLength(500).IsRequired();
        builder.Property(o => o.ScopeField).HasMaxLength(100);
        builder.Property(o => o.ScopeValue).HasMaxLength(500);
        builder.HasIndex(o => new { o.UserId, o.ExpiresAtUtc });

        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.DataScopeType)
            .WithMany()
            .HasForeignKey(o => o.DataScopeTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
