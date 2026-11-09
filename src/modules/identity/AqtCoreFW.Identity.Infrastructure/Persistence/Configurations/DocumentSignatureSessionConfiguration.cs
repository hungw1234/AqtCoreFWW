using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="DocumentSignatureSession"/> and its child
/// <see cref="DocumentSignatureSessionSigner"/> rows ([identity] schema).
///
/// Session: T2 entity — long Id PK (from AuditableEntity) + Guid PublicId unique AK.
/// Uses <see cref="EntityTypeConfigurationBase{T}"/> for the session so the T2 universals
/// (long PK, Guid AK, timestamps, soft-delete filter) are applied automatically.
///
/// Signers: child rows keyed by long Id (AuditableEntity base). One session has 1..N signer rows.
/// Unique constraint on (SessionId, SignerId) prevents the same signer appearing twice in one session.
/// </summary>
public sealed class DocumentSignatureSessionConfiguration
    : EntityTypeConfigurationBase<DocumentSignatureSession>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DocumentSignatureSession> builder)
    {
        builder.ToTable("DocumentSignatureSessions");

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.BpInstanceId).IsRequired();

        builder.Property(s => s.StepKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(s => s.ArtifactRef)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(s => s.Threshold)
            .IsRequired();

        // One session per (BpInstance, StepKey) per tenant — prevents duplicate gate creation for
        // the same transition step. The composite unique constraint is checked atomically on INSERT
        // so concurrent open-session calls race safely: exactly one wins, the other hits a unique
        // violation that the client maps to "return existing" via a subsequent load.
        builder.HasIndex(s => new { s.TenantId, s.BpInstanceId, s.StepKey })
            .IsUnique()
            .HasDatabaseName("UX_DocumentSignatureSession_TenantId_BpInstanceId_StepKey");

        // Navigation to ordered signer slots. UsePropertyAccessMode(Field) directs EF to populate
        // the backing _signers field directly (the public Signers property returns IReadOnlyList<>
        // which EF cannot set). The field name is discovered by the "_<PropertyName>" convention.
        builder.HasMany(s => s.Signers)
            .WithOne()
            .HasForeignKey(sl => sl.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Signers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>
/// EF Core mapping for <see cref="DocumentSignatureSessionSigner"/> child rows ([identity] schema).
/// Stand-alone IEntityTypeConfiguration — the parent config registers the HasMany nav; this config
/// owns the table name, column constraints, and child-level indexes.
/// </summary>
public sealed class DocumentSignatureSessionSignerConfiguration
    : IEntityTypeConfiguration<DocumentSignatureSessionSigner>
{
    public void Configure(EntityTypeBuilder<DocumentSignatureSessionSigner> builder)
    {
        builder.ToTable("DocumentSignatureSessionSigners");

        builder.HasKey(sl => sl.Id);

        builder.Property(sl => sl.TenantId).IsRequired();
        builder.Property(sl => sl.SessionId).IsRequired();
        builder.Property(sl => sl.SignerId).IsRequired();

        builder.Property(sl => sl.SequenceNumber)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(-1); // always emit explicit value on INSERT

        builder.Property(sl => sl.State)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValueSql("0") // Pending
            .HasSentinel((SignerSlotState)(-1));

        builder.Property(sl => sl.SigningRequestPublicId)
            .IsRequired(false);

        // Audit columns (from AuditableEntity)
        builder.Property(sl => sl.CreatedBy).IsRequired();
        builder.Property(sl => sl.ModifiedBy).IsRequired(false);
        builder.Property(sl => sl.CreatedAt).IsRequired();
        builder.Property(sl => sl.UpdatedAt).IsRequired(false);
        builder.Property(sl => sl.IsDeleted).IsRequired().HasDefaultValueSql("0").HasSentinel(true);
        builder.HasQueryFilter(sl => !sl.IsDeleted);

        // A signer may appear at most once per session.
        builder.HasIndex(sl => new { sl.SessionId, sl.SignerId })
            .IsUnique()
            .HasDatabaseName("UX_DocumentSignatureSessionSigner_SessionId_SignerId");

        // Supports "find next Pending signer in sequence order" query scoped to tenant (db_owner-safe).
        builder.HasIndex(sl => new { sl.TenantId, sl.SessionId, sl.SequenceNumber })
            .HasDatabaseName("IX_DocumentSignatureSessionSigner_TenantId_SessionId_Sequence");
    }
}
