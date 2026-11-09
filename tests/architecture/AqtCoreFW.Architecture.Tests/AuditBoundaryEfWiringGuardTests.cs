using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard for the audit read-isolation boundary (backlog <c>ISO-EF-FIRST-AUDIT-BOUNDARY</c>).
///
/// Scope of the claim (deliberately narrow — see the honesty note): every NON-AUDIT module's EF DbContext
/// registration must select its connection string through <see cref="NonAuditConnectionString"/> so that,
/// when a least-privilege <c>NonAudit</c> login (denied <c>[audit]</c>) is configured in production, those
/// modules cannot read <c>[audit]</c> through EF. The audit module registration must stay on the broad
/// <c>Default</c> login because it owns the schema.
///
/// This is a source-text guard (like <see cref="MarsConnectionStringGuardTests"/>). It protects the EF
/// wiring this slice shipped — a regression that reverts a module to <c>GetConnectionString("Default")</c>
/// drops the helper call and this guard goes red.
///
/// HONESTY / NOT CLAIMED HERE: this guard does NOT assert "audit is fully isolated". The standalone Dapper
/// read path (module-agnostic <c>DapperReadDbConnection</c>) still runs on <c>Default</c> and lands with the
/// deferred keyed-DI increment; the raw <c>new SqlConnection</c> sink allowlist belongs with that increment,
/// where raw-ADO isolation actually lands. UoW-active reads and admin/backup paths are documented ADR-012
/// exceptions.
/// </summary>
public sealed class AuditBoundaryEfWiringGuardTests
{
    // The helper call every non-audit module registration must route its DbContext connection string through.
    private const string HelperCall = "NonAuditConnectionString.Get(";

    // The 14 non-audit module Infrastructure registrations (census 2026-07-14). Audit is intentionally absent.
    private static readonly string[] NonAuditRegistrations =
    [
        Reg("workflow",       "AqtCoreFW.Workflow.Infrastructure",       "WorkflowInfrastructureRegistration"),
        Reg("rules",          "AqtCoreFW.Rules.Infrastructure",          "RulesInfrastructureRegistration"),
        Reg("businessprocess","AqtCoreFW.BusinessProcess.Infrastructure","BusinessProcessInfrastructureRegistration"),
        Reg("masterdata",     "AqtCoreFW.MasterData.Infrastructure",     "MasterDataInfrastructureRegistration"),
        Reg("files",          "AqtCoreFW.Files.Infrastructure",          "FilesInfrastructureRegistration"),
        Reg("forms",          "AqtCoreFW.Forms.Infrastructure",          "FormsInfrastructureRegistration"),
        Reg("reporting",      "AqtCoreFW.Reporting.Infrastructure",      "ReportingInfrastructureRegistration"),
        Reg("integration",    "AqtCoreFW.Integration.Infrastructure",    "IntegrationInfrastructureRegistration"),
        Reg("collaboration",  "AqtCoreFW.Collaboration.Infrastructure",  "CollaborationInfrastructureRegistration"),
        Reg("organization",   "AqtCoreFW.Organization.Infrastructure",   "OrganizationInfrastructureRegistration"),
        Reg("ai",             "AqtCoreFW.Ai.Infrastructure",             "AiInfrastructureRegistration"),
        Reg("notifications",  "AqtCoreFW.Notifications.Infrastructure",  "NotificationsInfrastructureRegistration"),
        Reg("identity",       "AqtCoreFW.Identity.Infrastructure",       "IdentityInfrastructureRegistration"),
        Reg("systemparams",   "AqtCoreFW.SystemParams.Infrastructure",   "SystemParamsInfrastructureRegistration"),
    ];

    private static readonly string AuditRegistration =
        Reg("audit", "AqtCoreFW.Audit.Infrastructure", "AuditInfrastructureRegistration");

    private static string Reg(string module, string project, string file)
        => Path.Combine("src", "modules", module, project, file + ".cs");

    // The 3 shared-infra registrations folded into the NonAudit helper (F8 follow-up — these are NOT
    // module registrations, so Reg() above (which is hardcoded to src/modules/...) cannot address them.
    private static readonly string[] SharedInfraRegistrations =
    [
        SharedInfra("Alerting", "AlertingRegistration"),
        SharedInfra("ApiKeys",  "ApiKeyRegistration"),
        SharedInfra("Webhooks", "WebhookRegistration"),
    ];

    // Messaging stays on Default (documented AuthServer-coupled exclusion, see the Fact below) —
    // MessagingDbContext backs the dead-letter/idempotency ledger MassTransit shares across the Api
    // and AuthServer hosts, so it cannot move onto a login AuthServer's own registration doesn't grant.
    private static readonly string MessagingRegistration =
        SharedInfra("Messaging", "MessagingRegistration");

    private static string SharedInfra(string area, string file)
        => Path.Combine("src", "shared", "AqtCoreFW.Infrastructure", area, file + ".cs");

    [Fact]
    public void EveryNonAuditModuleRegistration_RoutesConnectionStringThroughTheNonAuditHelper()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var found = 0;

        foreach (var relativePath in NonAuditRegistrations)
        {
            var fullPath = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                violations.Add($"{relativePath}: registration file not found (moved/renamed — update the guard).");
                continue;
            }

            found++;
            var text = File.ReadAllText(fullPath);
            if (!text.Contains(HelperCall, StringComparison.Ordinal))
                violations.Add(
                    $"{relativePath}: does not route its DbContext through {HelperCall}… — a non-audit module " +
                    $"must not bind EF straight to GetConnectionString(\"Default\").");
        }

        violations.Should().BeEmpty(
            because: "each non-audit module's EF reads must run under the NonAudit login when it is configured, " +
                     "so a leaked login cannot reach [audit] (ISO-EF-FIRST-AUDIT-BOUNDARY).");

        // Non-vacuity: a path typo that matches zero files would otherwise make the loop pass silently.
        found.Should().Be(NonAuditRegistrations.Length,
            "the guard must actually read every listed registration, not skip missing paths");
    }

    [Fact]
    public void AuditModuleRegistration_StaysOnTheBroadDefaultLogin()
    {
        var repoRoot = FindRepoRoot();
        var fullPath = Path.Combine(repoRoot, AuditRegistration);

        File.Exists(fullPath).Should().BeTrue($"{AuditRegistration} must exist");
        var text = File.ReadAllText(fullPath);

        text.Should().Contain("GetConnectionString(\"Default\")",
            because: "the audit module OWNS [audit] — it must read/write it on the broad Default login, not the " +
                     "NonAudit login that is denied [audit] (isolating audit from itself would break the chain).");
        text.Should().NotContain(HelperCall,
            because: "the audit registration must NOT route through the non-audit helper.");
    }

    // ── F8 follow-up: the 3 shared-infra registrations folded into the NonAudit helper ────────────
    [Fact]
    public void EverySharedInfraRegistration_RoutesConnectionStringThroughTheNonAuditHelper()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var found = 0;

        foreach (var relativePath in SharedInfraRegistrations)
        {
            var fullPath = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                violations.Add($"{relativePath}: registration file not found (moved/renamed — update the guard).");
                continue;
            }

            found++;
            var text = File.ReadAllText(fullPath);
            if (!text.Contains(HelperCall, StringComparison.Ordinal))
                violations.Add(
                    $"{relativePath}: does not route its DbContext through {HelperCall}… — a shared-infra " +
                    $"subsystem must not bind EF straight to GetConnectionString(\"Default\").");
        }

        violations.Should().BeEmpty(
            because: "the shared-infra subsystems (Alerting/ApiKeys/Webhooks) fold into the same EF wiring " +
                     "seam as the 14 module registrations above — they too must run under the NonAudit login " +
                     "when it is configured, closing the F8 review finding that Reg()'s src/modules-only path " +
                     "left this trio unguarded.");

        // Non-vacuity: a path typo that matches zero files would otherwise make the loop pass silently.
        found.Should().Be(SharedInfraRegistrations.Length,
            "the guard must actually read every listed shared-infra registration, not skip missing paths");
    }

    [Fact]
    public void MessagingRegistration_StaysOnTheBroadDefaultLogin_DocumentedAuthServerCoupledExclusion()
    {
        var repoRoot = FindRepoRoot();
        var fullPath = Path.Combine(repoRoot, MessagingRegistration);

        File.Exists(fullPath).Should().BeTrue($"{MessagingRegistration} must exist");
        var text = File.ReadAllText(fullPath);

        text.Should().NotContain(HelperCall,
            because: "MessagingRegistration is a documented exclusion (F8): MassTransit's dead-letter / " +
                     "idempotency ledger (MessagingDbContext) is shared across the Api AND AuthServer hosts. " +
                     "Moving it onto the NonAudit login would require AuthServer's own login-provisioning " +
                     "path to grant the same access, which is out of scope for this increment — folding it " +
                     "in silently would be a false claim of isolation this guard would not catch, so the " +
                     "exclusion is asserted explicitly instead of just being absent from SharedInfraRegistrations.");
    }

    /// <summary>
    /// Walks up from the test binary until it finds a directory containing both <c>src/</c> and <c>tests/</c>.
    /// Mirrors <see cref="MarsConnectionStringGuardTests"/>.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(
            Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            $"could not locate repo root walking up from {AppContext.BaseDirectory} (expected src/ + tests/)");
        return dir!.FullName;
    }
}
