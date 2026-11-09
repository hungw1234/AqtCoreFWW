using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for BusinessProcessDefinition aggregate root.
/// Composite indexes (TenantId, Id) + (TenantId, CreatedAt DESC) + IsDeleted auto-applied by EntityTypeConfigurationBase.
/// </summary>
public sealed class BusinessProcessDefinitionConfiguration
    : EntityTypeConfigurationBase<BusinessProcessDefinition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BusinessProcessDefinition> builder)
    {
        builder.ToTable("BusinessProcessDefinitions");

        builder.Property(b => b.TenantId).IsRequired();

        builder.Property(b => b.Code).HasMaxLength(100).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.Description).HasMaxLength(2000);

        builder.Property(b => b.WorkflowDefinitionId).IsRequired();
        builder.Property(b => b.IntakeFormTemplateId);
        builder.Property(b => b.IntakeFormTemplateVersion);

        builder.Property(b => b.IsActive).IsRequired();

        builder.Property(b => b.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Tenant-scoped unique code — admin slug per tenant.
        builder.HasIndex(b => new { b.TenantId, b.Code })
            .IsUnique()
            .HasDatabaseName("UX_BusinessProcessDefinitions_Tenant_Code");

        builder.HasIndex(b => new { b.TenantId, b.IsActive })
            .HasDatabaseName("IX_BusinessProcessDefinitions_Tenant_IsActive");

        builder.HasIndex(b => b.WorkflowDefinitionId)
            .HasDatabaseName("IX_BusinessProcessDefinitions_WorkflowDefinitionId");

        // Children — ClientCascade (T3-BP-FORMMAP-REPRO Path C, 2026-05-24).
        // Required relationship (FK non-nullable) means EF still auto-marks severed children
        // Deleted in ChangeTracker; the difference vs Cascade is that EF must EMIT explicit
        // DELETE statements at SaveChanges (instead of relying on DB-level ON DELETE CASCADE).
        // EF Core 10's batch sorter then orders DELETE-before-INSERT for same-table operations,
        // which unblocks UX_*_Bp_(State|Transition)_Form unique-index 2601 collisions when
        // ReplaceFormMaps re-adds an identical (BPId, *Id, FormTemplateId) triple after Clear*.
        // BP module does not hard-delete BusinessProcessDefinition rows (soft-delete via IsDeleted),
        // so loss of DB-level cascade is cosmetic for this aggregate.
        builder.HasMany(b => b.StateForms)
            .WithOne()
            .HasForeignKey(sf => sf.BusinessProcessDefinitionId)
            .OnDelete(DeleteBehavior.ClientCascade);

        builder.HasMany(b => b.TransitionForms)
            .WithOne()
            .HasForeignKey(tf => tf.BusinessProcessDefinitionId)
            .OnDelete(DeleteBehavior.ClientCascade);

        builder.Ignore(b => b.DomainEvents);
    }
}
