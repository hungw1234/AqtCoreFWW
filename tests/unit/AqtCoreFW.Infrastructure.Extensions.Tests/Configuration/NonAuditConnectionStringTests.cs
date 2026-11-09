using AqtCoreFW.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="NonAuditConnectionString.Get"/> — the pure precedence rule that points a
/// non-audit module's EF DbContext at the least-privilege <c>NonAudit</c> login when configured, else
/// the broad <c>Default</c>. Null-only fallback matches the existing <c>AuditAdmin ?? Default</c> idiom
/// (AuditInfrastructureRegistration), so an explicitly-set value always wins.
/// </summary>
public sealed class NonAuditConnectionStringTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void Get_NonAuditConfigured_TakesPrecedenceOverDefault()
    {
        var config = Config(
            ("ConnectionStrings:Default", "Server=.;Database=broad;"),
            ("ConnectionStrings:NonAudit", "Server=.;Database=scoped;User Id=nonaudit;"));

        NonAuditConnectionString.Get(config).Should().Be("Server=.;Database=scoped;User Id=nonaudit;");
    }

    [Fact]
    public void Get_NonAuditAbsent_FallsBackToDefault()
    {
        var config = Config(("ConnectionStrings:Default", "Server=.;Database=broad;"));

        NonAuditConnectionString.Get(config).Should().Be("Server=.;Database=broad;",
            "opt-in isolation: without a NonAudit login the module must keep booting on Default");
    }

    [Fact]
    public void Get_NeitherConfigured_ReturnsNull()
    {
        NonAuditConnectionString.Get(Config()).Should().BeNull(
            "no fabricated connection string — EF surfaces the missing-config failure, as it does for Default today");
    }

    // ── IsExplicitlyConfigured: tells "isolation active" apart from "opt-in fallback to Default" ──

    [Fact]
    public void IsExplicitlyConfigured_NonAuditSet_ReturnsTrue()
    {
        var config = Config(
            ("ConnectionStrings:Default", "Server=.;Database=broad;"),
            ("ConnectionStrings:NonAudit", "Server=.;Database=scoped;User Id=nonaudit;"));

        NonAuditConnectionString.IsExplicitlyConfigured(config).Should().BeTrue();
    }

    [Fact]
    public void IsExplicitlyConfigured_OnlyDefaultSet_ReturnsFalse()
    {
        var config = Config(("ConnectionStrings:Default", "Server=.;Database=broad;"));

        NonAuditConnectionString.IsExplicitlyConfigured(config).Should().BeFalse(
            "a Default-only config is the silent opt-in fallback the boundary check exists to surface");
    }

    [Fact]
    public void IsExplicitlyConfigured_NeitherSet_ReturnsFalse()
    {
        NonAuditConnectionString.IsExplicitlyConfigured(Config()).Should().BeFalse();
    }
}
