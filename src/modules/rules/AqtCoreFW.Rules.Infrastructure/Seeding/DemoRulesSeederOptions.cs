namespace AqtCoreFW.Rules.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="DemoRulesSeeder"/>. Bound from config section
/// <c>Rules:DemoRulesSeed</c>. Default OFF — demo data is opt-in per environment
/// (reset-all.ps1 flips it via env override; never on by default in prod).
/// </summary>
public sealed class DemoRulesSeederOptions
{
    /// <summary>Config section path.</summary>
    public const string SectionName = "Rules:DemoRulesSeed";

    /// <summary>When false (default) the seeder is a no-op.</summary>
    public bool Enabled { get; set; }
}
