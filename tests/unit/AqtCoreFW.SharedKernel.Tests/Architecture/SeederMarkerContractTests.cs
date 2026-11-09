using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Xunit;

namespace AqtCoreFW.SharedKernel.Tests.Architecture;

/// <summary>
/// Drift guard: every type whose name ends "Seeder" and implements IHostedService
/// MUST also implement IIntegrationTestSeeder.
///
/// Context: WebAppFixture (Phase-01) keeps only IIntegrationTestSeeder-marked services.
/// A new *Seeder that forgets the marker will silently run nowhere during integration
/// tests, hiding startup bugs exactly like the Phase-04a-RT incident.
///
/// Assemblies scanned: all assemblies that contain a known seeder.
///   - AqtCoreFW.Api                          (E2ETestDataSeeder + DemoBusinessProcessSeeder)
///   - AqtCoreFW.Identity.Infrastructure      (TenantSeeder + TenantRoleSeeder + TenantUserSeeder + TenantBackfillSeeder)
///   - AqtCoreFW.Organization.Infrastructure  (OrgTreeSeeder + TenantAssignmentSeeder)
///   - AqtCoreFW.MasterData.Infrastructure    (MasterDataSeeder)
///   - AqtCoreFW.SystemParams.Infrastructure  (SystemParamSeeder)
///   - AqtCoreFW.Notifications.Infrastructure (NotificationTemplateSeeder)
///   - AqtCoreFW.Infrastructure               (ApiKeyTestSeeder)
///   - AqtCoreFW.Workflow.Infrastructure      (DemoWorkflowSeeder)
///   - AqtCoreFW.Forms.Infrastructure         (DemoFormTemplateSeeder)
///   - AqtCoreFW.Integration.Infrastructure   (DemoIntegrationSeeder)
///   - AqtCoreFW.Rules.Infrastructure         (DemoRulesSeeder)
///   - AqtCoreFW.Reporting.Infrastructure     (DemoReportingSeeder)
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SeederMarkerContractTests
{
    // Force-load module assemblies by anchoring on a public type from each.
    // Without this, the assemblies may not be in AppDomain even though they're referenced.
    private static readonly Assembly[] SeederAssemblies =
    [
        typeof(AqtCoreFW.Api.E2ETestDataSeeder).Assembly,
        typeof(AqtCoreFW.Identity.Infrastructure.Seeding.TenantUserSeeder).Assembly,
        typeof(AqtCoreFW.Organization.Infrastructure.Seeding.OrgTreeSeeder).Assembly,
        typeof(AqtCoreFW.MasterData.Infrastructure.Services.MasterDataSeeder).Assembly,
        typeof(AqtCoreFW.SystemParams.Infrastructure.Services.SystemParamSeeder).Assembly,
        typeof(AqtCoreFW.Notifications.Infrastructure.NotificationTemplateSeeder).Assembly,
        typeof(AqtCoreFW.Infrastructure.ApiKeys.ApiKeyTestSeeder).Assembly,
        typeof(AqtCoreFW.Workflow.Infrastructure.Seeding.DemoWorkflowSeeder).Assembly,
        typeof(AqtCoreFW.Forms.Infrastructure.Seeding.DemoFormTemplateSeeder).Assembly,
        typeof(AqtCoreFW.Integration.Infrastructure.Seeding.DemoIntegrationSeeder).Assembly,
        typeof(AqtCoreFW.Rules.Infrastructure.Seeding.DemoRulesSeeder).Assembly,
        typeof(AqtCoreFW.Reporting.Infrastructure.Seeding.DemoReportingSeeder).Assembly,
    ];

    /// <summary>
    /// Positive test: every concrete *Seeder implementing IHostedService in the known
    /// module assemblies must also implement IIntegrationTestSeeder.
    ///
    /// Failure here means a seeder was added or modified without the marker — it will
    /// be silently stripped by WebAppFixture.ConfigureTestServices and its bugs will
    /// never surface during integration tests.
    /// </summary>
    [Fact]
    public void All_seeders_named_with_seeder_suffix_implement_IIntegrationTestSeeder()
    {
        var violations = SeederAssemblies
            .SelectMany(SafeGetTypes)
            .Where(IsConcreteSeeder)
            .Where(t => !typeof(IIntegrationTestSeeder).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        Assert.Empty(violations);

        // Redundant via Assert.Empty above, but provides richer message on failure:
        Assert.All(violations, name =>
            Assert.Fail(
                $"'{name}' implements IHostedService + name ends 'Seeder' but is missing IIntegrationTestSeeder marker. " +
                $"Add ': IIntegrationTestSeeder' to the class or WebAppFixture will silently strip it in integration tests."));
    }

    /// <summary>
    /// Negative / logic-lock test: defines an in-process dummy type that is named
    /// ending "Seeder", implements IHostedService, but does NOT implement
    /// IIntegrationTestSeeder. Asserts the marker check correctly flags it as missing.
    ///
    /// The dummy is a nested class (excluded from the positive sweep by the IsNested guard,
    /// so it never pollutes production test results). This test validates the core marker
    /// predicate — typeof(IIntegrationTestSeeder).IsAssignableFrom — independently,
    /// proving the positive test would fail if drift occurred.
    /// </summary>
    [Fact]
    public void Negative_assertion_logic_detects_missing_marker()
    {
        // Arrange: MissingMarkerDummySeeder is a concrete IHostedService whose name
        // ends "Seeder" but does NOT implement IIntegrationTestSeeder.
        // It is nested (excluded from positive sweep) — tested here via direct typeof.
        var dummyType = typeof(MissingMarkerDummySeeder);

        // Verify it satisfies the shape conditions (IHostedService + name suffix).
        Assert.True(
            typeof(IHostedService).IsAssignableFrom(dummyType),
            "MissingMarkerDummySeeder must implement IHostedService for the logic-lock to be meaningful.");
        Assert.True(
            dummyType.Name.EndsWith("Seeder", StringComparison.Ordinal),
            "MissingMarkerDummySeeder name must end 'Seeder' for the logic-lock to be meaningful.");
        Assert.True(
            dummyType is { IsClass: true, IsAbstract: false },
            "MissingMarkerDummySeeder must be a concrete class for the logic-lock to be meaningful.");

        // Core assertion: the marker check correctly returns false for a type without the marker.
        var hasMarker = typeof(IIntegrationTestSeeder).IsAssignableFrom(dummyType);
        Assert.False(hasMarker,
            "MissingMarkerDummySeeder intentionally lacks IIntegrationTestSeeder — " +
            "this proves the positive test would flag real drift if a seeder forgot the marker.");
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Predicate matching the same filter used by WebAppFixture's hosted-service stripping:
    /// concrete class + name ends "Seeder" + implements IHostedService.
    /// </summary>
    private static bool IsConcreteSeeder(Type t) =>
        t is { IsClass: true, IsAbstract: false, IsNested: false } &&
        t.Name.EndsWith("Seeder", StringComparison.Ordinal) &&
        typeof(IHostedService).IsAssignableFrom(t);

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    // ─── dummy type (nested — excluded by IsNested:false guard) ─────────────────

    /// <summary>
    /// Intentionally marker-free stub used ONLY by the negative test.
    /// Nested so it is excluded from the positive sweep (IsNested: false guard above).
    /// </summary>
    private sealed class MissingMarkerDummySeeder : IHostedService
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct)  => Task.CompletedTask;
    }
}
