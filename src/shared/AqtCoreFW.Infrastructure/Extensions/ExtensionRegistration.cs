using AqtCoreFW.SharedKernel.Extensions;

namespace AqtCoreFW.Infrastructure.Extensions;

/// <summary>
/// DI registration for the Extension Framework (M17).
/// Call services.AddExtensionFramework() from InfrastructureRegistration.
/// Individual modules register their handlers via
/// services.AddExtensionHandler&lt;THandler, TInput, TOutput&gt;().
/// </summary>
public static class ExtensionRegistration
{
    /// <summary>Registers ExtensionRegistry + ExtensionExecutor as singletons.</summary>
    public static IServiceCollection AddExtensionFramework(this IServiceCollection services)
    {
        services.AddSingleton<ExtensionRegistry>();
        services.AddSingleton<IExtensionExecutor, ExtensionExecutor>();
        return services;
    }

    /// <summary>
    /// Registers a concrete extension handler so it is discoverable by ExtensionRegistry.
    /// The handler is registered as both IExtensionHandlerMarker (for scanning)
    /// and IExtensionHandler&lt;TInput,TOutput&gt; (for typed resolution).
    /// Lifetime is singleton — handlers should be stateless.
    /// </summary>
    public static IServiceCollection AddExtensionHandler<THandler, TInput, TOutput>(
        this IServiceCollection services)
        where THandler : class, IExtensionHandler<TInput, TOutput>, IExtensionHandlerMarker
    {
        // Register as the concrete type so both interfaces share the same instance
        services.AddSingleton<THandler>();
        services.AddSingleton<IExtensionHandlerMarker>(sp => sp.GetRequiredService<THandler>());
        services.AddSingleton<IExtensionHandler<TInput, TOutput>>(sp => sp.GetRequiredService<THandler>());
        return services;
    }
}
