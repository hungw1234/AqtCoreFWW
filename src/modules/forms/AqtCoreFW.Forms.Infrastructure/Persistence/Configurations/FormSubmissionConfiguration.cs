using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormSubmissionConfiguration : EntityTypeConfigurationBase<FormSubmission>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable("FormSubmissions");

        builder.Property(s => s.FormTemplateId).IsRequired();
        builder.Property(s => s.FormTemplateVersion).IsRequired();
        builder.Property(s => s.SubmittedBy).IsRequired();
        builder.Property(s => s.SubmittedAt).IsRequired();

        // SubmissionData is nullable: populated for Json mode, null for Table mode
        builder.Property(s => s.SubmissionData)
            .HasColumnType("nvarchar(max)")
            .IsRequired(false);

        // Approval workflow columns
        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(SubmissionStatus.Pending)
            .IsRequired();

        builder.Property(s => s.ReviewedBy).IsRequired(false);
        builder.Property(s => s.ReviewedAt).IsRequired(false);

        builder.Property(s => s.ReviewComment)
            .HasMaxLength(2000)
            .IsRequired(false);

        // PDPL consent tracking
        builder.Property(s => s.ConsentGiven).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.ConsentRecordId).IsRequired(false);

        // Workflow integration
        builder.Property(s => s.WorkflowInstanceId).IsRequired(false);
        builder.HasIndex(s => s.WorkflowInstanceId).HasFilter("[WorkflowInstanceId] IS NOT NULL");

        // BP-Run state form linkage — long? FK to workflow.WorkflowStates.Id (T1 PK).
        // No EF navigation: cross-module FK enforced at DB layer; query-latest is the read pattern.
        builder.Property(s => s.WorkflowStateId).IsRequired(false);

        // Covering filtered index — supports "latest submission per (instance, state, form)" lookup.
        // Filtered to BP-Run rows only (WorkflowInstanceId IS NOT NULL AND WorkflowStateId IS NOT NULL).
        // Plan-doc'd risk: missing TenantId leading column accepted; EF global filter injects WHERE TenantId.
        // Sort on SubmittedAt is implicit ASC at index level; ORDER BY SubmittedAt DESC at query reads tail.
        builder.HasIndex(s => new { s.WorkflowInstanceId, s.WorkflowStateId, s.FormTemplateId, s.SubmittedAt })
               .HasDatabaseName("IX_FormSubmissions_LatestStateForm")
               .HasFilter("[WorkflowInstanceId] IS NOT NULL AND [WorkflowStateId] IS NOT NULL")
               .IncludeProperties(s => new { s.Id, s.Status, s.SubmittedBy });

        // Per-role signer evidence collection — populated for multi-signer templates only.
        // Single-signer evidence remains on the scalar columns below.
        builder.HasMany(s => s.SignerEvidences)
            .WithOne()
            .HasForeignKey(e => e.FormSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // PDF signing correlation — nullable; set at sign-initiation, never overwritten (at-most-once).
        // Signed PDF key is read via signing request projection, not stored here.
        builder.Property(s => s.SigningRequestId)
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);

        // Digital signature evidence — single-signer path (verify-at-ingest) — all nullable, no index.
        builder.Property(s => s.SignatureVerdict).IsRequired(false);
        builder.Property(s => s.SignerSubject).HasMaxLength(512).IsRequired(false);
        builder.Property(s => s.SignerSerial).HasMaxLength(128).IsRequired(false);
        builder.Property(s => s.SignatureVerifiedAt).IsRequired(false);
        builder.Property(s => s.SignatureChainEvidence).HasColumnType("nvarchar(max)").IsRequired(false);
        builder.Property(s => s.SignerCertDer).HasColumnType("varbinary(max)").IsRequired(false);

        // Composite indexes for common filter queries.
        // Base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped.
        builder.HasIndex(s => new { s.TenantId, s.FormTemplateId });
        builder.HasIndex(s => new { s.TenantId, s.SubmittedBy });
        builder.HasIndex(s => new { s.TenantId, s.SubmittedAt });
        builder.HasIndex(s => new { s.TenantId, s.Status });
    }
}
