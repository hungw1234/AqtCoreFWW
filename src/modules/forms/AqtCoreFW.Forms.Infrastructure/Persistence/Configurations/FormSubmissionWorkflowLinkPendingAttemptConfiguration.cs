using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormSubmissionWorkflowLinkPendingAttemptConfiguration
    : EntityTypeConfigurationBase<FormSubmissionWorkflowLinkPendingAttempt>
{
    protected override void ConfigureEntity(
        EntityTypeBuilder<FormSubmissionWorkflowLinkPendingAttempt> builder)
    {
        builder.ToTable("FormSubmissionWorkflowLinkPendingAttempts");

        builder.Property(a => a.SubmissionId).IsRequired();
        builder.Property(a => a.FormTemplateId).IsRequired();
        builder.Property(a => a.ApprovalWorkflowDefinitionId).IsRequired();
        builder.Property(a => a.SubmittedBy).IsRequired();
        builder.Property(a => a.AttemptCount).IsRequired().HasDefaultValue(0);

        builder.Property(a => a.LastFailureCode)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.LastError)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(a => a.NextRetryAt)
            .HasColumnType("datetimeoffset(3)")
            .IsRequired(false);

        // Stored as string for human-readable DB inspection (matches FormSubmission.Status precedent).
        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(WorkflowLinkPendingStatus.Pending)
            .IsRequired();

        builder.Property(a => a.ResolvedAt)
            .HasColumnType("datetimeoffset(3)")
            .IsRequired(false);
        builder.Property(a => a.ResolvedBy).IsRequired(false);

        // Idempotency unique key — UPSERT handler relies on this for duplicate-event short-circuit.
        builder.HasIndex(a => new { a.SubmissionId, a.TenantId })
            .IsUnique()
            .HasDatabaseName("UX_FormSubmissionWfLinkPendingAttempts_Submission_Tenant");

        // Retry-job scan: cheap query "rows due for retry".
        builder.HasIndex(a => new { a.TenantId, a.Status, a.NextRetryAt })
            .HasDatabaseName("IX_FormSubmissionWfLinkPendingAttempts_Tenant_Status_NextRetryAt")
            .HasFilter("[NextRetryAt] IS NOT NULL");

        // Admin UI: list pending by template.
        builder.HasIndex(a => new { a.TenantId, a.FormTemplateId, a.Status })
            .HasDatabaseName("IX_FormSubmissionWfLinkPendingAttempts_Tenant_Template_Status");
    }
}
