using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Hosted background service driving the outbox dispatcher loop.
/// Resolves a scoped <see cref="OutboxDispatcher"/> per cycle (so DbContexts and
/// other scoped dependencies stay short-lived). Runs every <see cref="OutboxOptions.PollingIntervalSeconds"/>.
/// Phase 02c (v2.133) replaces the v2.105 stub implementation.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "OutboxProcessor started. PollingIntervalSeconds={Interval} EnableDispatcher={Enabled}.",
            options.CurrentValue.PollingIntervalSeconds,
            options.CurrentValue.EnableDispatcher);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxProcessor cycle threw an unexpected error.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.CurrentValue.PollingIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("OutboxProcessor stopped.");
    }

    private async Task DispatchOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchBatchAsync(ct);
    }
}
