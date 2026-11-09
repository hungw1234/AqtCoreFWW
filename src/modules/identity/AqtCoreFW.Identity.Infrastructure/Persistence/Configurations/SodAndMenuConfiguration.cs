using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for SodConflictRule.
/// Table: identity.SodConflictRules
/// Unique: (PermissionCodeA, PermissionCodeB, TenantId) — prevents duplicate pairs.
/// Note: pair (A,B) and (B,A) are treated as equivalent at the application layer.
/// </summary>
public sealed class SodConflictRuleConfiguration : EntityTypeConfigurationBase<SodConflictRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SodConflictRule> builder)
    {
        builder.ToTable("SodConflictRules");

        builder.Property(r => r.PermissionCodeA)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.PermissionCodeB)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Severity)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasMaxLength(500);

        // Unique per ordered pair + tenant. Manual TenantId index kept — SodConflictRule not ITenantScoped.
        builder.HasIndex(r => new { r.PermissionCodeA, r.PermissionCodeB, r.TenantId })
            .IsUnique();

        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => r.IsActive);
    }
}

/// <summary>
/// EF Core configuration for AppMenu.
/// Table: identity.AppMenus
/// Unique: (Code, TenantId) — stable machine-readable code per tenant.
/// Self-referencing FK: ParentId → Id (nullable, no cascade to avoid cycles).
/// </summary>
public sealed class AppMenuConfiguration : EntityTypeConfigurationBase<AppMenu>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AppMenu> builder)
    {
        builder.ToTable("AppMenus");

        builder.Property(m => m.Code)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.Description)
            .HasMaxLength(500);

        builder.Property(m => m.Icon)
            .HasMaxLength(100);

        builder.Property(m => m.Route)
            .HasMaxLength(500);

        // Code unique per tenant. Manual TenantId index kept — AppMenu not ITenantScoped.
        builder.HasIndex(m => new { m.Code, m.TenantId }).IsUnique();
        builder.HasIndex(m => m.TenantId);
        builder.HasIndex(m => m.ParentId);

        // Self-referencing FK — ParentId (Guid?) references parent.PublicId (Guid AK), not Id (long PK)
        builder.HasOne<AppMenu>()
            .WithMany()
            .HasPrincipalKey(m => m.PublicId)
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasMany(m => m.RolePermissions)
            .WithOne(rp => rp.Menu)
            .HasForeignKey(rp => rp.MenuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// EF Core configuration for MenuRolePermission.
/// Table: identity.MenuRolePermissions
/// Composite PK: (MenuId, PermissionCode).
/// </summary>
public sealed class MenuRolePermissionConfiguration : IEntityTypeConfiguration<MenuRolePermission>
{
    public void Configure(EntityTypeBuilder<MenuRolePermission> builder)
    {
        builder.ToTable("MenuRolePermissions");

        // Composite primary key
        builder.HasKey(rp => new { rp.MenuId, rp.PermissionCode });

        // Mirror the principal's soft-delete: AppMenu carries a global !IsDeleted filter, so a
        // permission row of a soft-deleted menu must be filtered out too — keeps the data correct
        // and prevents the required Menu navigation resolving to null in a filtered query.
        builder.HasQueryFilter(rp => !rp.Menu.IsDeleted);

        builder.Property(rp => rp.PermissionCode)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(rp => rp.PermissionCode);
    }
}
