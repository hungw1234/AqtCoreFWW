namespace AqtCoreFW.Forms.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="DemoFormTemplateSeeder"/>. Bound from config section
/// <c>Forms:DemoFormTemplateSeed</c>. Default OFF — demo data is opt-in per environment
/// (reset-all.ps1 flips it via env override; never on by default in prod).
/// </summary>
public sealed class DemoFormTemplateSeederOptions
{
    /// <summary>Config section path.</summary>
    public const string SectionName = "Forms:DemoFormTemplateSeed";

    /// <summary>When false (default) the seeder is a no-op.</summary>
    public bool Enabled { get; set; }
}
