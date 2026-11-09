using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(r => r.Code).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);

        // RoleType stored as int column
        builder.Property(r => r.RoleType).HasConversion<int>();

        builder.HasIndex(r => r.TenantId);

        // Code must be unique within a tenant (null TenantId = system-wide role)
        builder.HasIndex(r => new { r.Code, r.TenantId }).IsUnique();

        // Roles are tenant-scoped, so a role's display name must be unique PER TENANT, not globally.
        // Identity's base maps a global-unique index on NormalizedName (RoleNameIndex) — drop it and
        // re-scope to (TenantId, NormalizedName) so two tenants can share a name (e.g. "Bộ trưởng").
        var normalizedName = builder.Metadata.FindProperty(nameof(ApplicationRole.NormalizedName));
        if (normalizedName is not null)
        {
            var globalNameIndex = builder.Metadata.FindIndex(normalizedName);
            if (globalNameIndex is not null)
                builder.Metadata.RemoveIndex(globalNameIndex);
        }
        builder.HasIndex(r => new { r.TenantId, r.NormalizedName })
            .IsUnique()
            .HasFilter("[NormalizedName] IS NOT NULL");

        builder.HasMany(r => r.RolePermissions)
            .WithOne(rp => rp.Role)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.GroupRoles)
            .WithOne(gra => gra.Role)
            .HasForeignKey(gra => gra.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
