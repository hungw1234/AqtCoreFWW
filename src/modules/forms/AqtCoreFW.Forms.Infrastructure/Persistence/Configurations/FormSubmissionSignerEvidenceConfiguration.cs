using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class FormSubmissionSignerEvidenceConfiguration : EntityTypeConfigurationBase<FormSubmissionSignerEvidence>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FormSubmissionSignerEvidence> builder)
    {
        builder.ToTable("FormSubmissionSignerEvidences");

        builder.Property(e => e.FormSubmissionId).IsRequired();
        builder.Property(e => e.SignerRoleKey).HasMaxLength(128).IsRequired();
        builder.Property(e => e.SignatureVerdict).IsRequired();
        builder.Property(e => e.SignerSubject).HasMaxLength(512).IsRequired(false);
        builder.Property(e => e.SignerSerial).HasMaxLength(128).IsRequired(false);
        builder.Property(e => e.SignatureVerifiedAt).IsRequired();
        builder.Property(e => e.SignedAt).IsRequired();
        builder.Property(e => e.SignerUserId).IsRequired(false);

        // Audit-grade chain evidence — unbounded JSON
        builder.Property(e => e.SignatureChainEvidence)
            .HasColumnType("nvarchar(max)")
            .IsRequired(false);

        // Public cert bytes — kept for offline re-verification
        builder.Property(e => e.SignerCertDer)
            .HasColumnType("varbinary(max)")
            .IsRequired(false);

        // Lookup pattern: all evidence rows for a given submission + role
        builder.HasIndex(e => new { e.FormSubmissionId, e.SignerRoleKey });
    }
}
