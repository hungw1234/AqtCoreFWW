using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Resolves how a host treats EF Core schema migrations at startup, from CLI args + configuration.
///
/// Background (backlog <c>RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS</c>): the hosts call
/// <c>DbContext.Database.MigrateAsync()</c> for every module context on every boot, which forces the
/// runtime DB login to keep DDL rights (<c>db_ddladmin</c> — a <c>SECURITY POLICY</c> can only be
/// <c>ALTER</c>ed/disabled by a DDL-capable principal). That weakens the RLS least-privilege story
/// (see <c>docs/deployment-guide.md</c> §4 "Application DB Principal"). This gate lets ops:
/// <list type="bullet">
///   <item><b><c>--migrate-only</c></b> CLI arg or <b><c>RUN_MIGRATIONS_ONLY=true</c></b> config/env —
///   run migrations, then exit; used by the Helm <c>db-migrate</c> Job / a K8s init-container running
///   under a separate <em>elevated</em> login.</item>
///   <item><b><c>SKIP_STARTUP_MIGRATIONS=true</c></b> config/env — the runtime container skips
///   <c>MigrateAsync</c> entirely (schema is migrated out-of-band), so the runtime login can drop
///   <c>db_ddladmin</c>.</item>
/// </list>
/// Default (neither set): migrate on boot — keeps dev, test (Testcontainers) and single-container
/// deployments working with no extra wiring.
///
/// <c>--migrate-only</c> takes precedence over <c>SKIP_STARTUP_MIGRATIONS</c>
/// (see <see cref="RunMigrations"/>), so the migrate Job inheriting <c>SKIP_STARTUP_MIGRATIONS</c>
/// from a shared ConfigMap is harmless.
///
/// Scope: schema migrations only. Data-plane upgraders (PII encryption / email-hash backfill) run
/// unconditionally on normal runtime boot regardless of this gate — they only need <c>db_datawriter</c>.
/// </summary>
public sealed record StartupMigrationMode(bool MigrateOnly, bool SkipStartupMigrations)
{
    /// <summary>CLI flag: run schema migrations then exit without starting the host.</summary>
    public const string MigrateOnlyArg = "--migrate-only";

    /// <summary>Config/env key (boolean) equivalent to <see cref="MigrateOnlyArg"/>.</summary>
    public const string RunMigrationsOnlyKey = "RUN_MIGRATIONS_ONLY";

    /// <summary>Config/env key (boolean): skip startup migrations on a normal runtime boot.</summary>
    public const string SkipStartupMigrationsKey = "SKIP_STARTUP_MIGRATIONS";

    /// <summary>Whether the host should run <c>MigrateAsync</c> for its DbContexts during startup.</summary>
    public bool RunMigrations => MigrateOnly || !SkipStartupMigrations;

    /// <summary>
    /// Whether the host should exit immediately after migrations instead of serving requests.
    /// (In this mode the runtime-only startup checks — e.g. <c>WarnIfDbOwnerInProduction</c> — are also
    /// skipped via the early return, since the migrate invocation legitimately uses the elevated login.)
    /// </summary>
    public bool ExitAfterMigrations => MigrateOnly;

    /// <summary>True if <paramref name="args"/> contains the <see cref="MigrateOnlyArg"/> token.</summary>
    public static bool HasMigrateOnlyArg(string[] args) =>
        args is not null && Array.Exists(args, a => string.Equals(a, MigrateOnlyArg, StringComparison.Ordinal));

    /// <summary>
    /// Returns <paramref name="args"/> with the <see cref="MigrateOnlyArg"/> token removed, so it is not
    /// passed to <c>WebApplication.CreateBuilder</c> — a bare <c>--migrate-only</c> token (no value) makes
    /// <c>CommandLineConfigurationProvider</c> throw "Unrecognized argument format".
    /// </summary>
    public static string[] StripKnownArgs(string[] args) =>
        args is null
            ? []
            : Array.FindAll(args, a => !string.Equals(a, MigrateOnlyArg, StringComparison.Ordinal));

    /// <summary>
    /// Resolves the mode. Call AFTER <c>builder.Build()</c> so env vars / appsettings have flowed into
    /// <paramref name="config"/>; pass the result of <see cref="HasMigrateOnlyArg"/> evaluated BEFORE
    /// <c>CreateBuilder</c> (where the arg is stripped) as <paramref name="hasMigrateOnlyArg"/>.
    /// </summary>
    public static StartupMigrationMode Resolve(bool hasMigrateOnlyArg, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new StartupMigrationMode(
            MigrateOnly: hasMigrateOnlyArg || ParseBool(config[RunMigrationsOnlyKey]),
            SkipStartupMigrations: ParseBool(config[SkipStartupMigrationsKey]));
    }

    /// <summary>Lenient boolean parse: accepts <c>"true"</c>/<c>"false"</c> (case-insensitive) and <c>"1"</c>/<c>"0"</c>; null/blank/anything else → false.</summary>
    public static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed == "1") return true;
        if (trimmed == "0") return false;
        return bool.TryParse(trimmed, out var parsed) && parsed;
    }
}
