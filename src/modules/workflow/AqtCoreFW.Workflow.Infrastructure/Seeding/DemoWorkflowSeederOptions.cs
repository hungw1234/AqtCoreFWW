namespace AqtCoreFW.Workflow.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="DemoWorkflowSeeder"/>. Bound from config section
/// <c>Workflow:DemoWorkflowSeed</c>. Default OFF — demo data is opt-in per environment
/// (reset-all.ps1 flips it via env override; never on by default in prod).
/// </summary>
public sealed class DemoWorkflowSeederOptions
{
    /// <summary>Config section path.</summary>
    public const string SectionName = "Workflow:DemoWorkflowSeed";

    /// <summary>When false (default) the seeder is a no-op.</summary>
    public bool Enabled { get; set; }
}
