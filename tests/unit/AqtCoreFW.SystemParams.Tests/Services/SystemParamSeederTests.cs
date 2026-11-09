using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.SystemParams.Tests.Services;

/// <summary>
/// Unit tests for SystemParamSeeder's feature-flag cache warm.
///
/// The flag L0 cache is loaded once at host start; on a cold DB that initial load runs before the
/// seeder writes its rows, so a flag would read false until the 5-minute periodic refresh — a file
/// uploaded in that gap bypasses the virus-scan gate. The seeder closes that window by reloading the
/// cache right after it writes. These tests prove the cache is warm immediately after StartAsync,
/// without any manual/periodic reload.
///
/// Design note: DbContext is registered Singleton so the seeder's write scope and the
/// FeatureFlagService read scope share one in-memory instance (unit-test only — never in production).
/// </summary>
public sealed class SystemParamSeederTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FeatureFlagService _flags;

    public SystemParamSeederTests()
    {
        var dbName = $"SystemParamSeederTests_{Guid.NewGuid()}";
        var services = new ServiceCollection();

        var tenantCtx = Substitute.For<AqtCoreFW.SharedKernel.Domain.ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);
        services.AddSingleton(tenantCtx);

        services.AddDbContext<SystemParamsDbContext>(
            opt => opt.UseInMemoryDatabase(dbName),
            contextLifetime: ServiceLifetime.Singleton,
            optionsLifetime: ServiceLifetime.Singleton);

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _flags = new FeatureFlagService(_scopeFactory, NullLogger<FeatureFlagService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SystemParamSeeder MakeSeeder(string environment)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        return new SystemParamSeeder(_scopeFactory, env, _flags, NullLogger<SystemParamSeeder>.Instance);
    }

    [Fact]
    public async Task StartAsync_DevEnvironment_WarmsVirusScanFlag_WithoutPeriodicReload()
    {
        var seeder = MakeSeeder(Environments.Development);

        // No FeatureFlagService.ReloadAllAsync call here — the cache starts empty. Only the seeder's
        // internal warm can make the flag read true.
        await seeder.StartAsync(CancellationToken.None);

        _flags.IsEnabled("feature:files.virus-scan").Should().BeTrue(
            "the seeder must warm the L0 cache for the dev-only virus-scan flag it just wrote");
    }

    [Fact]
    public async Task StartAsync_NonDevEnvironment_SeedsVirusScanFlagExplicitlyFalse()
    {
        var seeder = MakeSeeder(Environments.Production);

        await seeder.StartAsync(CancellationToken.None);

        // Scanning stays OFF outside dev — but the ROW must exist. IsEnabled cannot tell "seeded false"
        // from "never seeded" (both read false), which is exactly the ambiguity CLAMAV-PROD-FLAG-POLICY
        // was about: nobody could distinguish a deliberate opt-out from forgotten provisioning. Assert on
        // the persisted row, not just the cache, or this test passes for the wrong reason.
        _flags.IsEnabled("feature:files.virus-scan").Should().BeFalse(
            "scanning must not turn itself on before clamd is provisioned");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemParamsDbContext>();
        var row = await db.SystemParameters
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Key == "feature:files.virus-scan" && p.TenantId == null);

        row.Should().NotBeNull(
            "the flag is seeded in every environment so the posture is a value, not an absence");
        row!.Value.Should().Be("false");
        row.IsEditable.Should().BeTrue(
            "SystemParameter.UpdateValue throws GOV_CFG_001 on a non-editable row — seeding this false " +
            "would make the flag permanently unflippable with no other test catching it");
    }

    [Fact]
    public async Task StartAsync_WarmsAlwaysSeededFeatureFlags()
    {
        var seeder = MakeSeeder(Environments.Production);

        await seeder.StartAsync(CancellationToken.None);

        // audit_log / notifications are seeded true in every environment — proves the warm runs even
        // when the dev-only flag is absent.
        _flags.IsEnabled("feature:audit_log").Should().BeTrue();
        _flags.IsEnabled("feature:notifications").Should().BeTrue();
        _flags.IsEnabled("feature:ai_assistant").Should().BeFalse();
    }
}
