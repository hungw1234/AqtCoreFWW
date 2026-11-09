using RabbitMQ.Client;

namespace AqtCoreFW.Infrastructure.HealthChecks;

/// <summary>
/// Hands the RabbitMQ health check a single long-lived connection.
///
/// The async AddRabbitMQ overload invokes its factory on every probe and never disposes what it
/// gets back ("factory will be called each time the healthcheck route is requested" — the package's
/// own remarks), so a factory that opens a connection per call leaks one per readiness poll until
/// the broker runs out of file descriptors. Caching here keeps that to one connection, reopened
/// only if the broker drops it.
/// </summary>
public sealed class ProbeConnectionSource(Func<Task<IConnection>> connect)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync()
    {
        if (_connection is { IsOpen: true } live)
            return live;

        await _gate.WaitAsync();
        try
        {
            // Re-check: a concurrent probe may have reconnected while this one queued.
            if (_connection is { IsOpen: true } reconnected)
                return reconnected;

            if (_connection is not null)
            {
                // Closed connection — drop it before replacing, or the socket lingers.
                try { await _connection.DisposeAsync(); } catch (Exception) { /* already broken */ }
            }

            _connection = await connect();
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }
}
