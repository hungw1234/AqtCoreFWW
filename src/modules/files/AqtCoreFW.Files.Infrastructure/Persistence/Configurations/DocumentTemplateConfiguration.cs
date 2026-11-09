using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Files.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for DocumentTemplate aggregate root (T1: long Id PK, no PublicId AK).
/// Code is unique per tenant — used as stable integration identifier.
/// TemplateContent is stored as nvarchar(max) to accommodate large Scriban templates.
/// DomainEvents ignored — dispatched via outbox post-SaveChanges.
/// Phase-4b B3b: use EntityTypeConfigurationBase&lt;T&gt; (single-arg; base handles Id/soft-delete/timestamps).
/// </summary>
public sealed class DocumentTemplateConfiguration
    : EntityTypeConfigurationBase<DocumentTemplate>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("DocumentTemplates");

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Code).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.OutputFormat).IsRequired();
        builder.Property(t => t.TemplateContent).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.Status).IsRequired();
        builder.Property(t => t.TenantId).IsRequired();

        // Code uniqueness per tenant
        builder.HasIndex(t => new { t.TenantId, t.Code })
            .IsUnique()
            .HasDatabaseName("UX_DocumentTemplates_TenantId_Code");

        // List active templates per tenant
        builder.HasIndex(t => new { t.TenantId, t.Status })
            .HasDatabaseName("IX_DocumentTemplates_TenantId_Status");

        builder.Ignore(t => t.DomainEvents);
    }
}
