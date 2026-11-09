using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormFieldOptionConfiguration : EntityTypeConfigurationBase<FormFieldOption>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FormFieldOption> builder)
    {
        builder.ToTable("FormFieldOptions");

        builder.Property(o => o.FormFieldId).IsRequired();
        builder.Property(o => o.Value).HasMaxLength(500).IsRequired();
        builder.Property(o => o.LabelVi).HasMaxLength(500).IsRequired();
        builder.Property(o => o.LabelEn).HasMaxLength(500).IsRequired();
        builder.Property(o => o.DisplayOrder).IsRequired();

        // Index for ordered option retrieval per field
        builder.HasIndex(o => new { o.FormFieldId, o.DisplayOrder });
    }
}
