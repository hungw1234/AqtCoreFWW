using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// Idempotent seeder: creates a k6 test API key when SEED_TEST_APIKEY=true.
/// Dev/staging only — guarded by env var. Never runs in Production.
/// Key plaintext is logged once at Warning level — operator must copy to CI secret.
/// </summary>
public sealed class ApiKeyTestSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<ApiKeyTestSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private const string TestKeyName = "k6-test-runner";

    private static readonly IReadOnlyList<string> TestScopes =
    [
        "files.read", "files.write",
        "audit.read",
        "notifications.read",
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        // Guard: only run when explicitly enabled (dev/staging)
        var seedFlag = Environment.GetEnvironmentVariable("SEED_TEST_APIKEY");
        if (!string.Equals(seedFlag, "true", StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiKeyDbContext>();

        // Idempotent: skip if key already exists
        if (await db.ApiKeys.AnyAsync(k => k.Name == TestKeyName, ct))
        {
            logger.LogInformation("ApiKeyTestSeeder: {KeyName} already exists, skipping", TestKeyName);
            return;
        }

        var svc = scope.ServiceProvider.GetRequiredService<ApiKeyService>();

        var tenantIdEnv = Environment.GetEnvironmentVariable("TEST_TENANT_ID");
        var tenantId = Guid.TryParse(tenantIdEnv, out var tid)
            ? tid
            : Guid.Parse("00000000-0000-0000-0000-000000000001");

        var result = await svc.CreateAsync(
            name: TestKeyName,
            clientId: "k6",
            tenantId: tenantId,
            createdBy: "system-seeder",
            expiresAt: null,        // non-expiring — required for 70-min soak tests
            scopes: TestScopes,
            ct: ct);

        // Log plaintext ONCE — operator must store this in CI secret K6_TEST_APIKEY
        logger.LogWarning(
            "ApiKeyTestSeeder: k6 test key created. Store in CI secret K6_TEST_APIKEY: {Key}",
            result.Plaintext);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
