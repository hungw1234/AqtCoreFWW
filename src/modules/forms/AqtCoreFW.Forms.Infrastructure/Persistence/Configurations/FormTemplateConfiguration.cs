using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormTemplateConfiguration : EntityTypeConfigurationBase<FormTemplate>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FormTemplate> builder)
    {
        builder.ToTable("FormTemplates");

        // T2 AK — external addressing key; UNIQUE index mirrors IPublicIdentified contract.
        builder.Property(t => t.PublicId).IsRequired();
        builder.HasIndex(t => t.PublicId).IsUnique().HasDatabaseName("AK_FormTemplate_PublicId");

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameVi).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Version).IsRequired();

        // Code: unique slug per tenant — used for SQL view naming
        builder.Property(t => t.Code).HasMaxLength(100).IsRequired();

        // StorageMode and Status stored as string for readability in DB
        builder.Property(t => t.StorageMode)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Unique Code per tenant
        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();

        // Global uniqueness of PUBLIC Codes: the anonymous by-Code resolution runs cross-tenant, so at
        // most one IsPublic=1 template may own a given Code system-wide — otherwise that lookup is
        // arbitrary. Filtered unique index is the DB backstop that makes the resolution provably
        // unambiguous. Predicate mirrors the app-guard in SetFormTemplatePublicAccessCommandHandler
        // exactly (IsPublic=1 AND IsDeleted=0); status-agnostic so a Draft public template cannot
        // reserve a colliding Code that would become live on publish.
        builder.HasIndex(t => t.Code)
            .IsUnique()
            .HasFilter("[IsPublic] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_FormTemplate_PublicCode");

        // Common query patterns. Base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped.
        builder.HasIndex(t => new { t.TenantId, t.Status });

        // Fields navigation — owned by template
        builder.HasMany(t => t.Fields)
            .WithOne()
            .HasForeignKey(f => f.FormTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Signer role slots — part of the generated-document definition, cascade with template
        builder.HasMany(t => t.SignerRoles)
            .WithOne()
            .HasForeignKey(r => r.FormTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // PDPL consent config
        builder.Property(t => t.RequiresConsent).HasDefaultValue(false).IsRequired();
        builder.Property(t => t.ConsentText).HasMaxLength(2000).IsRequired(false);

        // Anonymous public accessibility — DB default 0 == CLR default false (no HasSentinel needed).
        // Bound into the FormTemplates RLS FILTER public-read carve-out.
        builder.Property(t => t.IsPublic).HasDefaultValue(false).IsRequired();

        // Workflow integration — optional approval workflow
        builder.Property(t => t.ApprovalWorkflowDefinitionId).IsRequired(false);

        // M-02: Optimistic concurrency — prevents silent overwrites on concurrent template edits
        // ValueGeneratedOnAddOrUpdate ensures SQL Server auto-generates the timestamp
        builder.Property(t => t.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken()
            .IsRequired(false);

        // Domain events not persisted — outbox handles external events
        builder.Ignore(t => t.DomainEvents);
    }
}
