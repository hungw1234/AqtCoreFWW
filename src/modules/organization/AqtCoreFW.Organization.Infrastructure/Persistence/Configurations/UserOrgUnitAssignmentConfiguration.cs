using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Organization.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Organization.Infrastructure.Persistence.Configurations;

public sealed class UserOrgUnitAssignmentConfiguration : EntityTypeConfigurationBase<UserOrgUnitAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserOrgUnitAssignment> e)
    {
        e.ToTable("UserOrgUnitAssignments");
        e.Property(x => x.RoleInOrg).HasMaxLength(100).IsRequired();
        e.HasIndex(x => new { x.TenantId, x.UserId, x.IsActive });
        e.HasIndex(x => new { x.TenantId, x.OrgUnitId });
    }
}
