namespace AqtCoreFW.SharedKernel.Extensions;

/// <summary>
/// Non-generic marker interface implemented by all IExtensionHandler&lt;TInput,TOutput&gt; classes.
/// Enables DI scanning without needing to know TInput/TOutput at registration time.
/// Every concrete handler MUST implement both this interface and IExtensionHandler&lt;TInput,TOutput&gt;.
/// </summary>
public interface IExtensionHandlerMarker
{
    string ExtensionPointKey { get; }
    int Priority { get; }
}
