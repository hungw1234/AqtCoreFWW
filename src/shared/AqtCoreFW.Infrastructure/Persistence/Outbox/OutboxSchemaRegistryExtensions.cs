using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// DI extensions for declaring module schemas that participate in the outbox dispatcher.
/// Each module's InfrastructureRegistration calls <c>services.AddOutboxSchema("&lt;schema&gt;")</c>.
/// </summary>
public static class OutboxSchemaRegistryExtensions
{
    /// <summary>
    /// Registers a module schema with the outbox dispatcher allowlist.
    /// Idempotent. Adds <see cref="OutboxSchemaRegistry"/> as singleton on first call.
    /// </summary>
    public static IServiceCollection AddOutboxSchema(
        this IServiceCollection services,
        string schemaName)
    {
        var registry = ResolveOrAddRegistry(services);
        registry.Register(schemaName);
        return services;
    }

    private static OutboxSchemaRegistry ResolveOrAddRegistry(IServiceCollection services)
    {
        // Find existing registration to keep registry mutable across multiple AddOutboxSchema calls
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IOutboxSchemaRegistry));

        if (descriptor?.ImplementationInstance is OutboxSchemaRegistry existing)
            return existing;

        var registry = new OutboxSchemaRegistry();
        services.RemoveAll<IOutboxSchemaRegistry>();
        services.AddSingleton<IOutboxSchemaRegistry>(registry);
        return registry;
    }
}
