namespace AqtCoreFW.Api;

/// <summary>
/// Options for <see cref="DemoBusinessProcessSeeder"/>. Bound from config section
/// <c>BusinessProcess:DemoBusinessProcessSeed</c>. Default OFF — demo data is opt-in per
/// environment (reset-all.ps1 flips it via env override; never on by default in prod).
///
/// The seeder is host-resident (not in BusinessProcess.Infrastructure) because it is the first
/// inherently cross-module seeder: it reads the demo Workflow definition + Form template to wire
/// the BP bindings. Module assemblies may not reference each other (ModuleIsolationTests), so the
/// composition root — which already references all module Infrastructure — is the sanctioned home,
/// mirroring E2ETestDataSeeder. The config section keeps the BusinessProcess: prefix for grouping.
/// </summary>
public sealed class DemoBusinessProcessSeederOptions
{
    /// <summary>Config section path.</summary>
    public const string SectionName = "BusinessProcess:DemoBusinessProcessSeed";

    /// <summary>When false (default) the seeder is a no-op.</summary>
    public bool Enabled { get; set; }
}
