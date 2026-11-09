using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.SystemParams.Infrastructure.Services;

/// <summary>Seeds 5 default system params + 3 feature flags at startup (idempotent).</summary>
public class SystemParamSeeder(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IFeatureFlagService featureFlags,
    ILogger<SystemParamSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemParamsDbContext>();

        await db.Database.EnsureCreatedAsync(ct);

        // Per-key guard (replaces the old AnyAsync gate). The old gate skipped seeding
        // entirely once ANY row existed, which blocked additions to this list from
        // reaching dev DBs. session-config controller queries `session_timeout_minutes`
        // + `session_countdown_seconds` explicitly — absence = silent hardcoded fallback.
        var defaults = new (string Key, string Value, SystemParamDataType Type, string Desc, bool Editable)[]
        {
            ("app:name",                  "AqtCoreFW", SystemParamDataType.String, "Application name",                                              false),
            ("app:version",               "1.0.0",     SystemParamDataType.String, "Application version",                                           false),
            ("app:max_upload_mb",         "100",       SystemParamDataType.Int,    "Max file upload in MB",                                         true),
            ("feature:audit_log",         "true",      SystemParamDataType.Bool,   "Enable audit logging",                                          true),
            ("feature:notifications",     "true",      SystemParamDataType.Bool,   "Enable notifications",                                          true),
            ("feature:ai_assistant",      "false",     SystemParamDataType.Bool,   "Enable AI assistant",                                           true),
            ("feature:e_signature",       "false",     SystemParamDataType.Bool,   "Enable e-signature module",                                     true),
            // Session keys — consumed by SessionConfigController (GET /api/v1/session-config)
            ("session_timeout_minutes",   "30",        SystemParamDataType.Int,    "User session idle timeout in minutes (1-480)",                  true),
            ("session_countdown_seconds", "120",       SystemParamDataType.Int,    "Countdown before auto-logout after idle warning (10-300, default 120s)", true),
            // QĐ742 1.6.b — runtime kill-switch for MFA enrolment enforcement on cookie/refresh paths.
            // Default false: template repo is dev-only; admins flip to true to activate per-role MfaPolicy enforcement.
            // Tenant override supported via SystemParameter.TenantId column. ROPC path ignores this flag (compliance hard-block).
            ("mfa.enrolment.required",    "false",     SystemParamDataType.Bool,   "Enforce MFA enrolment redirect (cookie/refresh). Per-role MfaPolicy still applies when true. Tenant override supported.", true),
        };

        var seedSet = defaults.ToList();

        // Virus-scan posture is seeded in EVERY environment so the row always exists. Only the default
        // VALUE differs by environment.
        //
        // Flipping it: the admin endpoint (PUT /api/v1/admin/feature-flags/files.virus-scan) writes a
        // TENANT-SCOPED row, never this global one. That is why both enforcement gates read
        // IsEnabled(key, tenantId) — tenant override first, this row as the fallback. A deployment-wide
        // enable means updating this global row directly.
        //
        // Dev seeds `true`: the two-phase upload scan runs against the local clamd (docker-compose
        // `clamav` service / aqt-runapp).
        //
        // Every other environment seeds `false`, NOT "unseeded". FeatureFlagService.IsEnabled returns
        // false for a missing key, so an absent row and a deliberate opt-out were indistinguishable —
        // "nobody provisioned clamd yet" read exactly like "scanning is off on purpose", and the only
        // signal was a LogCritical per upload. An explicit false row makes the posture a visible,
        // editable value; VirusScanPostureHealthCheck then reports it as Degraded in Production.
        //
        // The value stays false until ops provisions clamd. Order matters: provision clamd FIRST, flip
        // the flag SECOND — the scanner is fail-closed, so flipping it on without a reachable clamd
        // holds every upload Quarantined rather than passing it unscanned.
        seedSet.Add(("feature:files.virus-scan",
            hostEnvironment.IsDevelopment() ? "true" : "false",
            SystemParamDataType.Bool,
            "Enable ClamAV virus scan on upload (two-phase upload phase 2). Requires a reachable clamd: the scanner is fail-closed, so enabling this without one holds uploads Quarantined.",
            true));

        // Gates the untrusted-PDF upload-then-sign path. Defaults OFF in EVERY environment (incl. dev):
        // signing a user-uploaded PDF safely needs cryptographic verification of the returned artifact,
        // which needs parsing an untrusted PDF inside a not-yet-built parse sandbox — that whole chain is
        // deferred to a later release. Global row (no tenant): deployment-wide posture, not a per-tenant
        // capability; a tenant must not opt itself in. Editable so QA/ops can flip it to exercise the path.
        seedSet.Add(("feature:signing.upload-arbitrary-pdf",
            "false",
            SystemParamDataType.Bool,
            "Enable signing of user-uploaded (untrusted) PDFs. Off by default: the untrusted-PDF crypto-verify sandbox chain is deferred to a later release. Form-generated PDF signing and detached-data signing are unaffected.",
            true));

        var existingKeys = await db.SystemParameters
            .Where(p => p.TenantId == null)
            .Select(p => p.Key)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingKeys, StringComparer.Ordinal);

        var toAdd = new List<SystemParameter>();
        foreach (var (key, value, type, desc, editable) in seedSet)
        {
            if (!existingSet.Contains(key))
            {
                toAdd.Add(SystemParameter.Create(key, value, type, desc, editable));
            }
        }

        if (toAdd.Count == 0)
        {
            logger.LogDebug("SystemParams: all defaults already present — skip");
            return;
        }

        await db.SystemParameters.AddRangeAsync(toAdd, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("SystemParams seeded: {Count} new default(s)", toAdd.Count);

        // Warm the feature-flag L0 cache for any flag this run just created. The flag cache is loaded
        // once at host start; on a cold DB that initial load runs before these rows exist, so a flag
        // would read false until the 5-min periodic refresh — a file uploaded in that gap bypasses the
        // virus-scan gate. Reloading here collapses that window to the seeding instant. Idempotent: a
        // later periodic reload simply re-reads the same rows.
        if (toAdd.Any(p => p.Key.StartsWith("feature:", StringComparison.Ordinal)))
        {
            await featureFlags.ReloadAllAsync(ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
