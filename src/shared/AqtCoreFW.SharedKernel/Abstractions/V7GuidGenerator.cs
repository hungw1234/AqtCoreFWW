namespace AqtCoreFW.SharedKernel.Abstractions;

/// <summary>
/// Production IGuidGenerator. Stateless — registered as Singleton in DI.
/// NewIdMonotonic delegates to .NET 10 Guid.CreateVersion7() (timestamp-prefixed, sub-ms ordering undefined per RFC 9562).
/// NewIdRandom delegates to Guid.NewGuid() (cryptographic v4).
/// </summary>
internal sealed class V7GuidGenerator : IGuidGenerator
{
    public Guid NewIdMonotonic() => Guid.CreateVersion7();

    public Guid NewIdRandom() => Guid.NewGuid();
}
