using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.SystemParams.Tests.Services;

/// <summary>
/// Unit tests for FeatureFlagService.
/// Tests L0 ConcurrentDict cache: IsEnabled tenant-fallback logic, Invalidate, and ReloadAllAsync
/// using an in-memory EF Core DbContext to exercise the real reload path.
///
/// Design note: DbContext is registered as Singleton so that the test's seed writes and the
/// scope-factory reads share the same in-memory DbContext instance. This is correct for unit
/// tests where we need deterministic in-process state; do not use Singleton in production.
/// </summary>
public sealed class FeatureFlagServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SystemParamsDbContext _db;
    private readonly FeatureFlagService _sut;

    public FeatureFlagServiceTests()
    {
        // Register SystemParamsDbContext as Singleton so the seed DbContext and the
        // scope-factory DbContext are the same instance (in-memory, unit test only).
        var dbName = $"FeatureFlagTests_{Guid.NewGuid()}";
        var services = new ServiceCollection();

        // Register ITenantContext stub — SystemParamsDbContext requires it
        var tenantCtx = Substitute.For<AqtCoreFW.SharedKernel.Domain.ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);
        services.AddSingleton(tenantCtx);

        services.AddDbContext<SystemParamsDbContext>(
            opt => opt.UseInMemoryDatabase(dbName),
            contextLifetime: ServiceLifetime.Singleton,
            optionsLifetime: ServiceLifetime.Singleton);

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<SystemParamsDbContext>();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _sut = new FeatureFlagService(scopeFactory, NullLogger<FeatureFlagService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    // --- helpers ---

    private SystemParameter FeatureFlag(string key, string value, string? tenantId = null) =>
        SystemParameter.Create(key, value, SystemParamDataType.Bool, "test flag", isEditable: true, tenantId);

    private async Task SeedAndReload(params SystemParameter[] flags)
    {
        _db.SystemParameters.AddRange(flags);
        await _db.SaveChangesAsync();
        await _sut.ReloadAllAsync();
    }

    // --- IsEnabled: unknown key ---

    [Fact]
    public async Task IsEnabled_UnknownKey_ReturnsFalse()
    {
        await _sut.ReloadAllAsync(); // empty DB

        _sut.IsEnabled("feature:unknown").Should().BeFalse();
    }

    // --- IsEnabled: global key ---

    [Fact]
    public async Task IsEnabled_GlobalKeyTrue_ReturnsTrue()
    {
        await SeedAndReload(FeatureFlag("feature:reports", "true"));

        _sut.IsEnabled("feature:reports").Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabled_GlobalKeyFalse_ReturnsFalse()
    {
        await SeedAndReload(FeatureFlag("feature:reports", "false"));

        _sut.IsEnabled("feature:reports").Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabled_GlobalKeyInvalidBoolValue_ReturnsFalse()
    {
        await SeedAndReload(FeatureFlag("feature:reports", "yes")); // not parseable as bool

        _sut.IsEnabled("feature:reports").Should().BeFalse();
    }

    // --- IsEnabled: tenant-specific ---

    [Fact]
    public async Task IsEnabled_TenantSpecificKey_ReturnsTenantValue()
    {
        await SeedAndReload(
            FeatureFlag("feature:reports", "false"),              // global = off
            FeatureFlag("feature:reports", "true", "tenant-42")); // tenant-42 = on

        _sut.IsEnabled("feature:reports", "tenant-42").Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabled_TenantKeyMissing_FallsBackToGlobal()
    {
        await SeedAndReload(FeatureFlag("feature:reports", "true")); // global only

        // tenant-99 has no specific override — falls back to global true
        _sut.IsEnabled("feature:reports", "tenant-99").Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabled_TenantKeyExists_DoesNotFallBackToGlobal()
    {
        await SeedAndReload(
            FeatureFlag("feature:reports", "true"),               // global = on
            FeatureFlag("feature:reports", "false", "tenant-42")); // tenant-42 = off

        // Tenant key found (false) — must not fall through to global (true)
        _sut.IsEnabled("feature:reports", "tenant-42").Should().BeFalse();
    }

    // --- IsEnabled: case-insensitive keys ---

    [Fact]
    public async Task IsEnabled_CaseInsensitiveKeys_ReturnsCorrectValue()
    {
        // SystemParameter.Create lowercases the key on store
        await SeedAndReload(FeatureFlag("feature:Reports", "true")); // stored as "feature:reports"

        _sut.IsEnabled("FEATURE:REPORTS").Should().BeTrue();
        _sut.IsEnabled("Feature:Reports").Should().BeTrue();
        _sut.IsEnabled("feature:reports").Should().BeTrue();
    }

    // --- Invalidate ---

    [Fact]
    public async Task Invalidate_RemovesKeyFromCache()
    {
        await SeedAndReload(FeatureFlag("feature:reports", "true"));
        _sut.IsEnabled("feature:reports").Should().BeTrue(); // pre-condition

        _sut.Invalidate("feature:reports");

        _sut.IsEnabled("feature:reports").Should().BeFalse();
    }

    [Fact]
    public void Invalidate_NonExistentKey_NoError()
    {
        // Must not throw even when key is absent
        var act = () => _sut.Invalidate("feature:nonexistent");

        act.Should().NotThrow();
    }

    // --- ReloadAllAsync ---

    [Fact]
    public async Task ReloadAllAsync_LoadsFlagsFromDatabase()
    {
        _db.SystemParameters.AddRange(
            FeatureFlag("feature:export", "true"),
            FeatureFlag("feature:import", "false"),
            // Non-feature param — must NOT be loaded
            SystemParameter.Create("app:timeout", "30", SystemParamDataType.Int, "timeout", isEditable: true));
        await _db.SaveChangesAsync();

        await _sut.ReloadAllAsync();

        _sut.IsEnabled("feature:export").Should().BeTrue();
        _sut.IsEnabled("feature:import").Should().BeFalse();
        _sut.IsEnabled("app:timeout").Should().BeFalse(); // filtered out
    }

    [Fact]
    public async Task ReloadAllAsync_ParsesBooleanValues()
    {
        _db.SystemParameters.AddRange(
            FeatureFlag("feature:a", "True"),
            FeatureFlag("feature:b", "FALSE"),
            FeatureFlag("feature:c", "1"));    // not bool — treated as false
        await _db.SaveChangesAsync();

        await _sut.ReloadAllAsync();

        _sut.IsEnabled("feature:a").Should().BeTrue();
        _sut.IsEnabled("feature:b").Should().BeFalse();
        _sut.IsEnabled("feature:c").Should().BeFalse();
    }

    [Fact]
    public async Task ReloadAllAsync_TenantScopedKeysStoredCorrectly()
    {
        _db.SystemParameters.AddRange(
            FeatureFlag("feature:dashboard", "true", "tenant-1"),
            FeatureFlag("feature:dashboard", "false", "tenant-2"));
        await _db.SaveChangesAsync();

        await _sut.ReloadAllAsync();

        _sut.IsEnabled("feature:dashboard", "tenant-1").Should().BeTrue();
        _sut.IsEnabled("feature:dashboard", "tenant-2").Should().BeFalse();
    }

    [Fact]
    public async Task ReloadAllAsync_OverwritesPreviousCacheValues()
    {
        // First load: flag is on
        await SeedAndReload(FeatureFlag("feature:beta", "true"));
        _sut.IsEnabled("feature:beta").Should().BeTrue();

        // Simulate value change in DB
        var param = await _db.SystemParameters.FirstAsync(p => p.Key == "feature:beta");
        param.UpdateValue("false");
        await _db.SaveChangesAsync();

        await _sut.ReloadAllAsync();

        _sut.IsEnabled("feature:beta").Should().BeFalse();
    }
}
