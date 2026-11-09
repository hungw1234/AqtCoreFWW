using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// Isolated DbContext for API key storage — schema "gateway".
/// Separate from module DbContexts to avoid cross-cutting EF query filter bleed.
/// </summary>
public sealed class ApiKeyDbContext(DbContextOptions<ApiKeyDbContext> options) : DbContext(options)
{
    public DbSet<ApiKeyRecord> ApiKeys => Set<ApiKeyRecord>();
    public DbSet<ApiKeyScope> ApiKeyScopes => Set<ApiKeyScope>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // Suppress PendingModelChangesWarning during startup migrations.
        // Model drift during development is expected until all pending migrations are applied.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("gateway");

        modelBuilder.Entity<ApiKeyRecord>(e =>
        {
            e.ToTable("ApiKeys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Prefix).HasMaxLength(8).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(200).IsRequired();
            e.Property(x => x.ScopesJson).HasMaxLength(2000).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => x.Prefix).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.HasMany(x => x.Scopes)
                .WithOne(s => s.ApiKey)
                .HasForeignKey(s => s.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKeyScope>(e =>
        {
            e.ToTable("ApiKeyScopes");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScopePermission).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.ApiKeyId, x.TenantId, x.ScopePermission }).IsUnique();
        });
    }
}
