namespace AqtCoreFW.SharedKernel.Extensions;

/// <summary>
/// Marker for a typed extension point.
/// TInput flows in, TOutput flows out.
/// Implement this to declare a named extension point that handlers can attach to.
/// </summary>
public interface IExtensionPoint<TInput, TOutput>
{
    string Key { get; }
    string Description { get; }
}
