namespace AqtCoreFW.SharedKernel.Extensions;

/// <summary>
/// Optional attribute to declare the extension point key and priority on a handler class.
/// Used by tooling/code-gen; runtime registration still goes through DI.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ExtensionHandlerAttribute(string key, int priority = 100) : Attribute
{
    public string Key { get; } = key;
    public int Priority { get; } = priority;
}
