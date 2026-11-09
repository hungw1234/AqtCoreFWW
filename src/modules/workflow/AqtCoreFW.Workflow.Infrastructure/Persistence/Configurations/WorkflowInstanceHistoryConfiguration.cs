using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowInstanceHistory — append-only audit trail.
/// No soft-delete: history rows must never be purged (NĐ53 retention requirement).
/// </summary>
public sealed class WorkflowInstanceHistoryConfiguration
    : IEntityTypeConfiguration<WorkflowInstanceHistory>
{
    public void Configure(EntityTypeBuilder<WorkflowInstanceHistory> builder)
    {
        builder.ToTable("WorkflowInstanceHistories");
        // T3 entity: InternalId is bigint IDENTITY PK; MessageId is Guid AK (v7 monotonic).
        builder.HasKey(h => h.InternalId);
        builder.Property(h => h.InternalId).ValueGeneratedOnAdd().UseIdentityColumn();
        builder.Property(h => h.MessageId).HasColumnType("uniqueidentifier").IsRequired();
        builder.HasIndex(h => h.MessageId).IsUnique().HasDatabaseName("AK_WorkflowInstanceHistory_MessageId");

        builder.Property(h => h.CreatedAt).HasColumnType("datetime2(3)");

        builder.Property(h => h.ActionName).HasMaxLength(100).IsRequired();
        builder.Property(h => h.Comment).HasMaxLength(2000);
        builder.Property(h => h.TransitionedAt).IsRequired();
        builder.Property(h => h.TransitionedBy).IsRequired();
        builder.Property(h => h.FromStateId).IsRequired();
        builder.Property(h => h.ToStateId).IsRequired();
        builder.Property(h => h.InstanceId).IsRequired();

        builder.HasIndex(h => new { h.InstanceId, h.TransitionedAt })
            .HasDatabaseName("IX_WorkflowInstanceHistories_InstanceId_TransitionedAt");

        // Covering index for "dossiers I have processed" query.
        // TransitionedBy is the leading column so the DB seeks the caller's participation slice
        // directly. History has no TenantId (tenant gate lives on the WorkflowInstances JOIN),
        // so TenantId cannot be added as leading column — this is intentional.
        builder.HasIndex(h => new { h.TransitionedBy, h.InstanceId })
            .HasDatabaseName("IX_WorkflowInstanceHistories_TransitionedBy_InstanceId");
    }
}
