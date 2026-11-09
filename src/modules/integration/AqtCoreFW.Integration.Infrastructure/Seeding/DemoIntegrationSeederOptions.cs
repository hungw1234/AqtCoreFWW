namespace AqtCoreFW.Integration.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="DemoIntegrationSeeder"/>. Bound from config section
/// <c>Integration:DemoIntegrationSeed</c>. Default OFF — demo data is opt-in per environment
/// (reset-all.ps1 flips it via env override; never on by default in prod).
/// </summary>
public sealed class DemoIntegrationSeederOptions
{
    /// <summary>Config section path.</summary>
    public const string SectionName = "Integration:DemoIntegrationSeed";

    /// <summary>When false (default) the seeder is a no-op.</summary>
    public bool Enabled { get; set; }
}
