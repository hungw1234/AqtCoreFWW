using Testcontainers.MsSql;
using Testcontainers.Redis;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Shared test fixture — starts SQL Server + Redis containers once per test session.
/// Owns the ApiFactory lifetime: applies migrations, runs seeders, then keeps the
/// factory alive for the entire test collection (containers stay up until DisposeAsync).
/// All test classes in [Collection("Integration")] share this single fixture instance.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public MsSqlContainer SqlContainer { get; } = new MsSqlBuilder()
        .WithPassword("YourStrong!Passw0rd")
        .WithCleanUp(true)
        .Build();

    public RedisContainer RedisContainer { get; } = new RedisBuilder()
        .WithCleanUp(true)
        .Build();

    public string SqlConnectionString { get; private set; } = string.Empty;
    public string RedisConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Live ApiFactory — created after migrations complete.
    /// Tests use this instance via IntegrationTestBase.Factory.
    /// </summary>
    public ApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start both containers in parallel to reduce cold-start time
        await Task.WhenAll(
            SqlContainer.StartAsync(),
            RedisContainer.StartAsync());

        SqlConnectionString = SqlContainer.GetConnectionString();
        RedisConnectionString = RedisContainer.GetConnectionString();

        // Environment variables have highest priority in .NET config and reach
        // builder.Configuration (ConfigurationManager) used by AddDbContext/Hangfire lambdas.
        // IWebHostBuilder.ConfigureAppConfiguration only adds to web host config layer.
        //
        // TIMING INVARIANT: AddGovRateLimiter reads "RateLimiting:Enabled" from builder.Configuration
        // during service registration in Program.cs (before ConfigureWebHost hooks run). The in-memory
        // override in ApiFactory.ConfigureAppConfiguration arrives too late — it only reaches the
        // IWebHost config layer, not the ConfigurationManager used at DI build time. Setting the env
        // var here ensures the no-op branch is taken at registration, regardless of WAF timing.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", SqlConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Hangfire", SqlConnectionString);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", RedisConnectionString);
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("MessageBus__Transport", "InMemory");
        Environment.SetEnvironmentVariable("RateLimiting__Enabled", "false");

        // Create the shared ApiFactory and apply migrations + seeders.
        // Factory is NOT disposed here — it stays alive for the entire collection lifetime.
        Factory = new ApiFactory(this);
        await Factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        // Clean up environment variables
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Hangfire", null);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", null);
        Environment.SetEnvironmentVariable("Vault__Enabled", null);
        Environment.SetEnvironmentVariable("MessageBus__Transport", null);
        Environment.SetEnvironmentVariable("RateLimiting__Enabled", null);

        // Dispose the factory before stopping containers
        if (Factory != null)
            await Factory.DisposeAsync();

        await Task.WhenAll(
            SqlContainer.DisposeAsync().AsTask(),
            RedisContainer.DisposeAsync().AsTask());
    }
}
