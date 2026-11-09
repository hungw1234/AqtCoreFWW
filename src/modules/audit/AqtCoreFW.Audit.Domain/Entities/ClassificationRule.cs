using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// QĐ 742 cấp 4 §4.4 — per-level data-handling rules for the four classification tiers.
/// One row per <see cref="DataClassificationLevel"/> (4 rows total, seeded; Level is PK).
/// Mutable by SystemAdmin only (UpdateClassificationRule command).
/// </summary>
public sealed class ClassificationRule
{
    /// <summary>Primary key: maps 1-to-1 with <see cref="DataClassificationLevel"/> enum value.</summary>
    public DataClassificationLevel Level { get; set; }

    /// <summary>Whether data at this level must be encrypted in the database at rest.</summary>
    public bool MustEncryptAtRest { get; set; }

    /// <summary>Whether data at this level may be exported from the system.</summary>
    public bool AllowExport { get; set; }

    /// <summary>Maximum retention in days. Null = no retention limit enforced at this tier.</summary>
    public int? MaxRetentionDays { get; set; }

    /// <summary>Identity of the last admin who modified this rule (or "system" for seeded rows).</summary>
    public string UpdatedBy { get; set; } = "system";

    /// <summary>UTC timestamp of the last modification.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>EF optimistic concurrency token. 8-byte SQL Server rowversion.</summary>
    public byte[] RowVersion { get; set; } = [];

    private ClassificationRule() { }

    /// <summary>Creates a rule row (used by EF HasData seeding).</summary>
    public static ClassificationRule Seed(
        DataClassificationLevel level,
        bool mustEncryptAtRest,
        bool allowExport,
        int? maxRetentionDays) => new()
    {
        Level = level,
        MustEncryptAtRest = mustEncryptAtRest,
        AllowExport = allowExport,
        MaxRetentionDays = maxRetentionDays,
        UpdatedBy = "system",
        UpdatedAt = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
        RowVersion = []
    };

    public void Update(bool mustEncryptAtRest, bool allowExport, int? maxRetentionDays, string updatedBy)
    {
        MustEncryptAtRest = mustEncryptAtRest;
        AllowExport = allowExport;
        MaxRetentionDays = maxRetentionDays;
        UpdatedBy = updatedBy;
        UpdatedAt = DateTime.UtcNow;
    }
}
