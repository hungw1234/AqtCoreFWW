using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.SharedKernel.Abstractions;

/// <summary>
/// DI registration for SharedKernel cross-cutting services.
/// Phase-2 Foundation Refactor (2026-05-03): registers IGuidGenerator (V7GuidGenerator).
/// Test fixtures override with FakeGuidGenerator via AddSingleton/AddTransient replacement after this call.
/// </summary>
public static class SharedKernelServiceCollectionExtensions
{
    /// <summary>
    /// Registers SharedKernel cross-cutting services: IGuidGenerator (V7GuidGenerator singleton).
    /// Idempotent — TryAddSingleton ensures host can register before this without conflict.
    /// </summary>
    public static IServiceCollection AddSharedKernelGuidGenerator(this IServiceCollection services)
    {
        services.AddSingleton<IGuidGenerator, V7GuidGenerator>();
        return services;
    }
}
