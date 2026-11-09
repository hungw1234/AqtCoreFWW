using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Source-text-parse wiring guard for the Phase-04 <c>forms-workflow-link-retry</c> recurring job —
/// same precedent as <see cref="DeferredModuleFlagMigrationFilterTests.ReportingRecurringJobRegistration_IsGatedOnTheFlag_WithRemoveOnOff"/>.
///
/// Why source-parse instead of a real boot assertion: EVERY boot-path integration/smoke fixture in
/// this repo (<c>ApiHostFixture</c>, <c>SeederSmokeFixture</c>, <c>DeferredModulesOffHostFixture</c>,
/// <c>WebAppFixture</c> via <see cref="AqtCoreFW.Tests.SharedFixtures.WebAppFixture"/>) sets
/// <c>Hangfire:RegisterRecurringJobs=false</c> — the static Hangfire <c>RecurringJob</c>/
/// <c>BackgroundJob</c> facades bind <c>JobStorage.Current</c> once per test-host process, so a
/// multi-host test suite that actually exercised the registration block would freeze that static onto
/// the FIRST booted host's now-disposed Testcontainer, breaking every later host boot. No existing
/// fixture can safely flip that flag to true, so this test instead asserts on the Program.cs SOURCE
/// TEXT that the registration exists, is inside the retry-wrapped recurring-job block, and uses the
/// idempotent AddOrUpdate upsert (never a raw Enqueue).
/// </summary>
public sealed class FormWorkflowLinkRetryJobRegistrationSourceTests
{
    [Fact]
    public void FormWorkflowLinkRetryJobRegistration_ExistsInsideTheRecurringJobBlock()
    {
        var programPath = Path.Combine(
            RepoRoot(), "src", "host", "AqtCoreFW.Api", "Program.cs");
        var source = File.ReadAllText(programPath);

        var blockStartIdx = source.IndexOf(
            "RecurringJobRegistrationRetry.RunWithRetry(", StringComparison.Ordinal);
        var registerIdx = source.IndexOf(
            "RecurringJob.AddOrUpdate<FormWorkflowLinkRetryJob>(", StringComparison.Ordinal);
        var jobIdIdx = source.IndexOf("\"forms-workflow-link-retry\"", StringComparison.Ordinal);

        blockStartIdx.Should().BeGreaterThan(0, "the retry-wrapped recurring-job registration block must exist");
        registerIdx.Should().BeGreaterThan(blockStartIdx,
            "FormWorkflowLinkRetryJob must be registered INSIDE the boot-lock retry wrapper, " +
            "like every sibling recurring job (SlaBreachCheckerJob, EscalationCheckJob, etc.) — " +
            "registering outside it would skip the transient-lock-timeout fail-soft retry.");
        jobIdIdx.Should().BeGreaterThan(registerIdx,
            "the Hangfire job id \"forms-workflow-link-retry\" must immediately follow the AddOrUpdate call");

        // Same-block sanity: confirm the registration precedes the closing of RunWithRetry's register
        // lambda by checking it appears before the next top-level recurring-job block's closing paren
        // sequence used by every sibling registration (logger: app.Services...).
        var closingLoggerIdx = source.IndexOf(
            "logger: app.Services.GetRequiredService<ILogger<AqtCoreFW.Api.Program>>());",
            StringComparison.Ordinal);
        closingLoggerIdx.Should().BeGreaterThan(registerIdx,
            "the registration must be inside the block, before the RunWithRetry closing call");
    }

    [Fact]
    public void FormWorkflowLinkRetryJobRegistration_IsGatedBySameTestingAndFlagChecksAsSiblings()
    {
        var programPath = Path.Combine(
            RepoRoot(), "src", "host", "AqtCoreFW.Api", "Program.cs");
        var source = File.ReadAllText(programPath);

        // The gate condition guarding the WHOLE recurring-job block (shared by every sibling
        // registration, including this one — no per-job gate needed since Forms.WorkflowLinkRetry
        // has no deferred-module flag, unlike Reporting).
        var gateIdx = source.IndexOf(
            "if (!app.Environment.IsEnvironment(\"Testing\") &&", StringComparison.Ordinal);
        var registerIdx = source.IndexOf(
            "RecurringJob.AddOrUpdate<FormWorkflowLinkRetryJob>(", StringComparison.Ordinal);

        gateIdx.Should().BeGreaterThan(0, "the shared Testing-environment + config-flag gate must exist");
        registerIdx.Should().BeGreaterThan(gateIdx,
            "FormWorkflowLinkRetryJob registration must be reached only through the same gate as every " +
            "other recurring job — Testing environment skip + Hangfire:RegisterRecurringJobs flag");
    }

    [Fact]
    public void FormWorkflowLinkRetryOptions_SectionBound_InFormsInfrastructureRegistration()
    {
        var registrationPath = Path.Combine(
            RepoRoot(), "src", "modules", "forms", "AqtCoreFW.Forms.Infrastructure",
            "FormsInfrastructureRegistration.cs");
        var source = File.ReadAllText(registrationPath);

        source.Should().Contain("services.Configure<FormWorkflowLinkRetryOptions>(",
            "FormWorkflowLinkRetryOptions must be bound from config, not left as code-only defaults");
        source.Should().Contain("FormWorkflowLinkRetryOptions.SectionName",
            "the bind must use the options class's own SectionName constant (Forms:WorkflowLinkRetry) " +
            "so the config key and the class stay in lockstep");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AqtCoreFW.sln"))
               && !File.Exists(Path.Combine(dir, "src", "AqtCoreFW.slnx")))
        {
            dir = Path.GetDirectoryName(dir)
                ?? throw new InvalidOperationException("Repo root not found from test base directory.");
        }
        return dir;
    }
}
