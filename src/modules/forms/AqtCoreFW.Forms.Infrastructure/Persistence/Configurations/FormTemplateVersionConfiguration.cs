using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for FormTemplateVersion — immutable snapshot table.
/// PK + soft-delete + tenant composite indexes auto-applied by EntityTypeConfigurationBase.
/// </summary>
public sealed class FormTemplateVersionConfiguration : EntityTypeConfigurationBase<FormTemplateVersion>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FormTemplateVersion> builder)
    {
        builder.ToTable("FormTemplateVersions");

        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.FormTemplateId).IsRequired();
        builder.Property(v => v.VersionNumber).IsRequired();

        builder.Property(v => v.SchemaJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        // One snapshot per (template, versionNumber) — append-only natural key.
        builder.HasIndex(v => new { v.FormTemplateId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("UX_FormTemplateVersion_Template_Version");

        // Tenant-scoped per-template list lookup (drives GET /form-templates/{id}/versions).
        builder.HasIndex(v => new { v.TenantId, v.FormTemplateId, v.VersionNumber })
            .HasDatabaseName("IX_FormTemplateVersion_Tenant_Template_Version");

        // Cascade from FormTemplate ensures snapshots disappear if the template is hard-deleted
        // (soft-delete leaves them intact — same global filter behavior).
        builder.HasOne<FormTemplate>()
            .WithMany()
            .HasForeignKey(v => v.FormTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
