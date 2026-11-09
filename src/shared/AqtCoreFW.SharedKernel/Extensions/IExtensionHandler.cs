namespace AqtCoreFW.SharedKernel.Extensions;

/// <summary>
/// Handler that extends behavior at a named extension point.
/// Register multiple handlers per key; they run in Priority order (lower = first).
/// </summary>
public interface IExtensionHandler<TInput, TOutput>
{
    /// <summary>The key that identifies which extension point this handler attaches to.</summary>
    string ExtensionPointKey { get; }

    /// <summary>Execution order — lower value runs first. Default convention: 100.</summary>
    int Priority { get; }

    Task<TOutput> HandleAsync(TInput input, CancellationToken ct = default);
}
