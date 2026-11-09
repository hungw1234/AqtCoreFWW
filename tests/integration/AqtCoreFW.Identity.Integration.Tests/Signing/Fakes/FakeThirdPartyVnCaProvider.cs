using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Integration.Tests.Signing.Fakes;

/// <summary>
/// Minimal third-party VN CA provider stub for D6 extensibility integration gate.
/// Simulates a future adapter (e.g. MiSA-CA, FPT-CA, NewCA) that:
///   - Registers itself via keyed + unkeyed DI with zero changes to core
///   - Must be discoverable via GET /available-providers without changing the resolver
///   - Must be callable via POST /sign when tenant config points to its key
///
/// Used exclusively in ProviderExtensibilityTests (integration suite).
/// Signature bytes [0xFA, 0xCE] are stable — integration tests assert on this value.
/// </summary>
internal sealed class FakeThirdPartyVnCaProvider : IDigitalSignatureProvider
{
    public string ProviderKey => "fake-thirdparty-ca";
    public string DisplayName => "Fake Third-Party CA (D6 extensibility test)";
    public SignatureProviderCapabilities Capabilities => SignatureProviderCapabilities.ServerSideSigning;

    public Task<SignResult> SignAsync(SignRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new SignResult(
            IsSuccess: true,
            Signature: [0xFA, 0xCE],
            SigningCertificate: null,
            FailureReason: null));

    public Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new VerifyResult(
            IsValid: true,
            FailureReason: null,
            SigningCertificate: null,
            ChainResult: null));
}
