using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for TenantSignatureConfig ([identity] schema, default from IdentityDbContext).
///
/// Indexes:
///   UQ_TenantSignatureConfig_TenantId — enforces one config per tenant (soft-delete aware).
///
/// Concurrency: RowVersion mapped via IsRowVersion() + IsConcurrencyToken() — EF appends
/// WHERE RowVersion = @p to UPDATE, throwing DbUpdateConcurrencyException on stale write.
///
/// OcspMode stored as int (HasConversion&lt;int&gt;) matching the MfaFactor enum precedent.
/// </summary>
public sealed class TenantSignatureConfigConfiguration : EntityTypeConfigurationBase<TenantSignatureConfig>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TenantSignatureConfig> builder)
    {
        // Provider key — foreign key into DI registry (matched at runtime by ISignatureProviderResolver).
        builder.Property(c => c.ProviderKey)
            .HasMaxLength(64)
            .IsRequired();

        // Vault key path (nullable — USB-token verify-only providers need no server credential).
        builder.Property(c => c.ApiCredentialsRef)
            .HasMaxLength(256)
            .IsRequired(false);

        // CSV thumbprints string — max 2048 covers ~40 SHA-1 roots with commas.
        builder.Property(c => c.AllowedCaRootThumbprints)
            .HasMaxLength(2048)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        // Store OcspMode enum as int column.
        builder.Property(c => c.OcspMode)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValueSql("0") // Soft = 0
            // Sentinel = a value the enum never holds, so EF always emits the explicit mode on
            // INSERT instead of treating Soft (the CLR-default 0) as "unset".
            .HasSentinel((OcspMode)(-1));

        // No store default: the app always sets IsEnabled explicitly (Create + Update both take
        // isEnabled), so a DB default is never relied upon. A bool sentinel is unsafe here — a
        // config is created both enabled and disabled, so any sentinel would suppress one legitimate
        // insert value and silently flip it. Dropping the redundant default (was DEFAULT 0, equal to
        // the CLR default) removes the residual model-build warning without changing behaviour.
        builder.Property(c => c.IsEnabled)
            .IsRequired();

        // Store SignatureFlowMode enum as int column. Same sentinel trick as OcspMode: the CLR-default 0 (Async)
        // is a legitimate value, so a (-1) sentinel forces EF to emit the explicit mode on INSERT rather than
        // treating Async as "unset".
        builder.Property(c => c.SignFlowMode)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValueSql("0") // Async = 0
            .HasSentinel((SignatureFlowMode)(-1));

        // Optimistic concurrency — admin UI may hold stale config across tab sessions.
        builder.Property(c => c.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // One config per tenant — enforced at DB level; soft-delete aware.
        builder.HasIndex(c => c.TenantId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UQ_TenantSignatureConfig_TenantId");
    }
}
