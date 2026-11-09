using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="SigningRequest"/> workflow aggregate ([identity] schema).
///
/// Uses <see cref="EntityTypeConfigurationBase{T}"/> so the T2 universals are applied automatically: long Id PK,
/// Guid PublicId unique AK (the value callers poll with), datetime2(3) timestamps, soft-delete filter + index,
/// and the tenant composite indexes. Audit columns (CreatedBy/ModifiedBy/ClassificationLevel) and the enum→int
/// conversions are handled by convention, matching the other AuditableEntity configs in this module.
///
/// There is deliberately NO status column: lifecycle is a read-time projection from the underlying provider
/// record (see <c>SignatureOrchestrator.GetStatusAsync</c>), so nothing here can drift from that source of truth.
/// </summary>
public sealed class SigningRequestConfiguration : EntityTypeConfigurationBase<SigningRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SigningRequest> builder)
    {
        builder.ToTable("SigningRequests");

        builder.Property(r => r.TenantId).IsRequired();

        builder.Property(r => r.ArtifactKind).HasConversion<int>().IsRequired();
        builder.Property(r => r.ArtifactRef).HasMaxLength(512).IsRequired();
        builder.Property(r => r.ProviderKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.AssignedSignerId).IsRequired();
        builder.Property(r => r.Placement).HasMaxLength(2048).IsRequired(false);

        builder.Property(r => r.UnderlyingKind).HasConversion<int>().IsRequired();
        builder.Property(r => r.UnderlyingRecordId).HasMaxLength(128).IsRequired(false);
        builder.Property(r => r.FailureReason).HasMaxLength(1024).IsRequired(false);

        // Signer position in an ordered multi-signer chain (1-based, default 1). Used by the
        // sequence guard to locate the predecessor request before allowing sequence N to begin.
        builder.Property(r => r.SignerSequenceNumber)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(-1); // sentinel so EF always emits an explicit value on INSERT

        // Correlates a Files module file to its signing request(s) so the Files endpoints can list
        // signing steps for a file without a foreign key on FileRecord. Filtered on non-null only:
        // UploadedPdf requests carry this; FormPdf / DetachedData requests store null.
        builder.Property(r => r.SourceFileId).IsRequired(false);
        builder.HasIndex(r => new { r.TenantId, r.SourceFileId })
            .HasFilter("[SourceFileId] IS NOT NULL")
            .HasDatabaseName("IX_SigningRequest_TenantId_SourceFileId");

        // Beyond the PublicId AK, the common lookup is "this signer's requests in this tenant"
        // (BP step status, signer dashboard).
        builder.HasIndex(r => new { r.TenantId, r.AssignedSignerId })
            .HasDatabaseName("IX_SigningRequest_TenantId_AssignedSignerId");

        // Supports the sequence guard query: find the request at a specific sequence for a given
        // (tenant, artifact) to verify the predecessor is Completed. TenantId scoping is explicit
        // so the check remains correct even when the connection runs as db_owner (RLS bypassed).
        builder.HasIndex(r => new { r.TenantId, r.ArtifactRef, r.SignerSequenceNumber })
            .HasDatabaseName("IX_SigningRequest_TenantId_ArtifactRef_SequenceNumber");
    }
}
