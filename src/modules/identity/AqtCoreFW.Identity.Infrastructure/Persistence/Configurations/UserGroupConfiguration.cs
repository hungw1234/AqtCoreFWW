using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class UserGroupConfiguration : EntityTypeConfigurationBase<UserGroup>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserGroup> builder)
    {
        builder.ToTable("UserGroups");

        builder.Property(g => g.Code).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(500);

        // Code unique per tenant. Manual TenantId index kept — UserGroup not ITenantScoped.
        builder.HasIndex(g => new { g.Code, g.TenantId }).IsUnique();
        builder.HasIndex(g => g.TenantId);

        builder.HasMany(g => g.Members)
            .WithOne(m => m.Group)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.RoleAssignments)
            .WithOne(gra => gra.Group)
            .HasForeignKey(gra => gra.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserGroupMembershipConfiguration : IEntityTypeConfiguration<UserGroupMembership>
{
    public void Configure(EntityTypeBuilder<UserGroupMembership> builder)
    {
        builder.ToTable("UserGroupMemberships");

        builder.HasKey(m => m.Id);

        // Mirror the principal's soft-delete: UserGroup carries a global !IsDeleted filter, so a
        // membership of a soft-deleted group must be filtered out too — both to keep the data
        // correct (a deleted group grants nothing) and to avoid the required Group navigation
        // resolving to null in a filtered query.
        builder.HasQueryFilter(m => !m.Group.IsDeleted);

        // One membership record per user per group
        builder.HasIndex(m => new { m.UserId, m.GroupId }).IsUnique();
        builder.HasIndex(m => m.GroupId);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GroupRoleAssignmentConfiguration : IEntityTypeConfiguration<GroupRoleAssignment>
{
    public void Configure(EntityTypeBuilder<GroupRoleAssignment> builder)
    {
        builder.ToTable("GroupRoleAssignments");

        builder.HasKey(gra => gra.Id);

        // Mirror the principal's soft-delete (see UserGroupMembership): a role assignment of a
        // soft-deleted group must not surface, and the required Group navigation must not go null.
        builder.HasQueryFilter(gra => !gra.Group.IsDeleted);

        // One assignment per group per role
        builder.HasIndex(gra => new { gra.GroupId, gra.RoleId }).IsUnique();
        builder.HasIndex(gra => gra.RoleId);
    }
}
