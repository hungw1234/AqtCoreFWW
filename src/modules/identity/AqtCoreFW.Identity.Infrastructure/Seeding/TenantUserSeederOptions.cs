namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="TenantUserSeeder"/>. Default OFF — enabled per-environment (Development +
/// the dev reset flow) where demo data is wanted; production inherits the default.
///
/// The per-tenant target count, code/email prefixes and domain live on the TenantSeedProfile; only
/// the seeder-mechanics knobs live here. <see cref="DevReserveCount"/> applies per tenant (each tenant
/// reserves this many {DevEmailPrefix}NNN users).
///
/// Owns the BTC + BNV identity user + user-role path. Enabled in Development (appsettings) and by the
/// reset-all script.
/// </summary>
public sealed class TenantUserSeederOptions
{
    public const string SectionName = "Identity:TenantUserSeed";

    /// <summary>Enable/disable user seeding on startup.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>EF batch size for bulk user / user-role insert.</summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>Deterministic seed for Bogus fake data — reseeded per tenant for reproducible runs.</summary>
    public int RandomSeed { get; set; } = 20260423;

    /// <summary>Reserved dev test users per tenant (emails {DevEmailPrefix}NNN{EmailDomain}).</summary>
    public int DevReserveCount { get; set; } = 129;
}
