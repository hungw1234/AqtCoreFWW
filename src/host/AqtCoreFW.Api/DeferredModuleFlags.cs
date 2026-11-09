using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Persistence;

namespace AqtCoreFW.Api;

/// <summary>
/// Config-flag gate for the five modules deferred from the v1.0 launch scope
/// (AI, Collaboration, BusinessProcess, Reporting, Rules — user decision 2026-07-16).
///
/// Each module is toggled by <c>Features:{Name}:Enabled</c>. A MISSING key means ENABLED —
/// dev/test environments keep today's everything-on behavior with zero config; production
/// turns modules off explicitly at deploy time. Registration-time code reads IConfiguration
/// only (never a DB-backed flag service): the gate runs during DI composition, before any
/// database is reachable, and a module that is off must contribute nothing — no services,
/// no controllers, no hosted services, no schema migration.
///
/// Turning a flag off removes the module at FOUR layers, all driven from this one class:
///   1. DI registration (Program.cs skips the Add*Module/Add*Infrastructure block),
///   2. controller discovery (its Presentation assembly is removed from the
///      ApplicationPartManager — required because project-referenced Presentation assemblies
///      are auto-discovered via DependencyContext regardless of DI, so without this step a
///      disabled module's endpoints would 500 on handler resolution instead of 404),
///   3. startup migration (its DbContext is filtered out of the migrated set, so a fresh
///      database never receives the schema),
///   4. endpoint mapping for non-controller surfaces (the Collaboration ChatHub).
/// </summary>
public static class DeferredModuleFlags
{
    /// <summary>Configuration section prefix; full key shape is Features:{Name}:Enabled.</summary>
    public const string SectionName = "Features";

    public const string Ai = "Ai";
    public const string Collaboration = "Collaboration";
    public const string BusinessProcess = "BusinessProcess";
    public const string Reporting = "Reporting";
    public const string Rules = "Rules";

    /// <summary>The five deferrable module names, for enumeration in gates and tests.</summary>
    public static readonly IReadOnlyList<string> ModuleNames =
        [Ai, Collaboration, BusinessProcess, Reporting, Rules];

    /// <summary>
    /// The DbContext each deferrable module migrates at startup — the migration-filter side
    /// of the gate. Kept here (next to the flag names) so adding a sixth deferrable module
    /// forces both halves to be updated together.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Type> ModuleDbContexts =
        new Dictionary<string, Type>
        {
            [Ai] = typeof(AiDbContext),
            [Collaboration] = typeof(CollaborationDbContext),
            [BusinessProcess] = typeof(BusinessProcessDbContext),
            [Reporting] = typeof(ReportingDbContext),
            [Rules] = typeof(RulesDbContext),
        };

    /// <summary>Missing key = enabled (default true) — see class remarks.</summary>
    public static bool IsEnabled(IConfiguration configuration, string moduleName) =>
        configuration.GetValue($"{SectionName}:{moduleName}:Enabled", defaultValue: true);

    /// <summary>The DbContext types belonging to modules that are currently DISABLED.</summary>
    public static IReadOnlySet<Type> DisabledDbContexts(IConfiguration configuration) =>
        ModuleNames
            .Where(name => !IsEnabled(configuration, name))
            .Select(name => ModuleDbContexts[name])
            .ToHashSet();
}
