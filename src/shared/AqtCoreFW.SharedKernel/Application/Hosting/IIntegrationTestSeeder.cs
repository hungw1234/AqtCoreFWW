using Microsoft.Extensions.Hosting;

namespace AqtCoreFW.SharedKernel.Application.Hosting;

/// <summary>
/// Marker for IHostedService implementations that perform one-shot data seeding
/// at startup. Integration test fixtures keep marked services registered while
/// stripping all other IHostedService entries (Hangfire, MassTransit, polling,
/// pub/sub) so seeder-specific bugs surface during tests instead of slipping
/// silently to prod startup (false-green pattern).
/// </summary>
public interface IIntegrationTestSeeder : IHostedService
{
}
