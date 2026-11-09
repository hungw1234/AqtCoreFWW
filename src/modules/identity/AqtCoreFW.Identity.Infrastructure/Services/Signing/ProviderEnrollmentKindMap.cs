using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Single source of truth mapping a signing <c>providerKey</c> to its required <see cref="EnrollmentKind"/>.
/// Lives in the Infrastructure signing-adapter layer (<c>Services/Signing/</c>) — the ONLY place vendor key
/// literals are permitted (D6 / QĐ742 keeps Identity.Domain + Identity.Application key-free); the allowlist
/// guard consumes it through the key-agnostic <see cref="IProviderEnrollmentKindMap"/> abstraction.
///
/// <para>Stateless — registered as a singleton. Reviving <c>remote-vnpt-smartca</c> = add
/// <c>EnrollmentKind.RemoteVnptSmartCa</c> + one entry below (same one-line cost as its DI registration), and
/// the guard auto-allows VNPT once matching enrollments exist. The <c>VNPT-SMARTCA-ADAPTER-DEPRECATE</c>
/// backlog row is REVERSED — do not delete the VNPT adapter/config the revival depends on.</para>
/// </summary>
public sealed class ProviderEnrollmentKindMap : IProviderEnrollmentKindMap
{
    // Case-insensitive: the shared resolver is the first, case-sensitive gate (unknown key → 404 before this
    // map is consulted), so a mixed-case registered key still resolves to its kind here without a bypass.
    private static readonly IReadOnlyDictionary<string, EnrollmentKind> Map =
        new Dictionary<string, EnrollmentKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["usb-vgca"]              = EnrollmentKind.UsbTokenVgca,
            ["remote-viettel-mysign"] = EnrollmentKind.RemoteViettelMysign,
            // ["remote-vnpt-smartca"] = EnrollmentKind.RemoteVnptSmartCa,  // added at VNPT SmartCA revival
        };

    /// <inheritdoc/>
    public bool TryResolve(string providerKey, out EnrollmentKind kind) =>
        Map.TryGetValue(providerKey, out kind);

    /// <summary>Every provider key with a mapping. The arch drift test asserts registered keys ⊆ this set.</summary>
    public IReadOnlyCollection<string> MappedKeys => (IReadOnlyCollection<string>)Map.Keys;
}
