using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormFieldConfiguration : EntityTypeConfigurationBase<FormField>
{
    // T1 entity: Id is DB-assigned IDENTITY (default Entity convention).
    // Prior ValueGeneratedNever() override caused EF tracker to treat default(long)=0 as
    // an explicit key, conflicting on multi-field adds (FormTemplate.AddField in seeder + AddFormFieldCommandHandler).
    protected override void ConfigureEntity(EntityTypeBuilder<FormField> builder)
    {
        builder.ToTable("FormFields");

        builder.Property(f => f.FormTemplateId).IsRequired();
        builder.Property(f => f.ParentFieldId).IsRequired(false);

        // FieldKey: SQL-safe identifier, max 128 chars, enforced by domain ValidateFieldKey()
        builder.Property(f => f.FieldKey).HasMaxLength(128).IsRequired();

        // Type stored as string for readability and forward-compatibility
        builder.Property(f => f.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(f => f.LabelVi).HasMaxLength(500).IsRequired();
        builder.Property(f => f.LabelEn).HasMaxLength(500).IsRequired();
        builder.Property(f => f.DisplayOrder).IsRequired();
        builder.Property(f => f.Required).IsRequired();
        builder.Property(f => f.IsActive).IsRequired();
        builder.Property(f => f.AddedInVersion).IsRequired();

        // Optional JSON blobs for validation, data source, and formula (Phase 2)
        builder.Property(f => f.ValidationRulesJson)
            .HasColumnType("nvarchar(2000)")
            .IsRequired(false);

        builder.Property(f => f.DataSourceJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired(false);

        builder.Property(f => f.FormulaJson)
            .HasColumnType("nvarchar(2000)")
            .IsRequired(false);

        builder.Property(f => f.VisibilityRulesJson)
            .HasColumnType("nvarchar(2000)")
            .IsRequired(false);

        builder.Property(f => f.RequiredIfJson)
            .HasColumnType("nvarchar(2000)")
            .IsRequired(false);

        // BP MVP P6: prefill source pointer JSON envelope (v1).
        // 2000-char cap mirrors VisibilityRulesJson — single-source pointer fits well under the limit.
        builder.Property(f => f.PrefillSourceJson)
            .HasColumnType("nvarchar(2000)")
            .IsRequired(false);

        // Unique active FieldKey per template (partial index — allows reuse of keys after deactivation)
        builder.HasIndex(f => new { f.FormTemplateId, f.FieldKey })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        // Options navigation
        builder.HasMany(f => f.Options)
            .WithOne()
            .HasForeignKey(o => o.FormFieldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
