using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowDefinition aggregate root.
/// States and Transitions stored in sibling tables (not JSON) for queryability.
/// DomainEvents ignored — dispatched via outbox post-SaveChanges.
/// </summary>
public sealed class WorkflowDefinitionConfiguration
    : EntityTypeConfigurationBase<WorkflowDefinition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinitions");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.IsActive).IsRequired();
        builder.Property(d => d.DefinitionKey).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Version).IsRequired().HasDefaultValue(1);
        builder.Property(d => d.IsLatest).IsRequired().HasDefaultValue(true);
        builder.Property(d => d.EntityType).HasMaxLength(64).IsRequired().HasDefaultValue("Generic");

        builder.HasIndex(d => new { d.TenantId, d.Name })
            .HasDatabaseName("IX_WorkflowDefinitions_TenantId_Name");

        // Versioning index: lookup latest version by key per tenant
        builder.HasIndex(d => new { d.TenantId, d.DefinitionKey, d.Version })
            .IsUnique()
            .HasDatabaseName("IX_WorkflowDefinitions_TenantId_Key_Version");

        // Fast lookup for latest version per key
        builder.HasIndex(d => new { d.TenantId, d.DefinitionKey, d.IsLatest })
            .HasDatabaseName("IX_WorkflowDefinitions_TenantId_Key_IsLatest")
            .HasFilter("[IsLatest] = 1 AND [IsDeleted] = 0");

        builder.HasMany(d => d.States)
            .WithOne()
            .HasForeignKey(s => s.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Transitions)
            .WithOne()
            .HasForeignKey(t => t.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(d => d.DomainEvents);
    }
}
