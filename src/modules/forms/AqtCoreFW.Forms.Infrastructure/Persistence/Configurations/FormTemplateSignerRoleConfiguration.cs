using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormTemplateSignerRoleConfiguration : EntityTypeConfigurationBase<FormTemplateSignerRole>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FormTemplateSignerRole> builder)
    {
        builder.ToTable("FormTemplateSignerRoles");

        builder.Property(r => r.FormTemplateId).IsRequired();
        builder.Property(r => r.RoleKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.RoleLabelVi).HasMaxLength(200).IsRequired();
        builder.Property(r => r.DisplayOrder).IsRequired();
        builder.Property(r => r.IsRequired).IsRequired();

        // PlacementJson: opaque anchor/coordinate JSON — unbounded by design
        builder.Property(r => r.PlacementJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired(false);

        // Ordered retrieval of signer roles per template
        builder.HasIndex(r => new { r.FormTemplateId, r.DisplayOrder });
    }
}
