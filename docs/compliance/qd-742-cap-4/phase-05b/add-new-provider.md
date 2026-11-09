# Add a New VN CA Provider — Runbook (QĐ 742 §5.1 D6)

**Audience:** Backend engineers integrating a new Vietnamese CA service.  
**Mandate:** Adding a provider must require **zero changes** to core (resolver, handlers, controllers). All extension happens at the periphery — one new class + one DI registration.

---

## Prerequisites

- Provider vendor SDK or API spec available.
- `IDigitalSignatureProvider` contract understood (see `Identity.Domain.Services`).
- `SignatureProviderCapabilities` flags understood: choose the correct combination.

---

## Step 1 — Create the provider class

Location: `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/Signing/`

```csharp
using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// [VendorName] VN CA integration.
/// Capabilities: declare what this provider supports (see SignatureProviderCapabilities).
/// </summary>
internal sealed class MyVendorSignatureProvider(
    IOptions<MyVendorOptions> options,
    ILogger<MyVendorSignatureProvider> logger)
    : IDigitalSignatureProvider
{
    // ── REQUIRED PROPERTIES ──────────────────────────────────────────────

    /// <summary>Unique stable key used to route tenant configs to this provider.</summary>
    public string ProviderKey => "my-vendor-key"; // kebab-case, e.g. "remote-bkav-ca"

    /// <summary>
    /// Declare your capabilities honestly:
    ///   ServerSideSigning   = server calls vendor API to sign (VNPT pattern)
    ///   ClientSideSigning   = FE/client calls local agent (VGCA USB pattern)
    ///   RequiresLocalAgent  = requires local WSS bridge (always pair with ClientSideSigning)
    ///   LongTermValidation  = provider embeds LTV timestamp in signature
    ///   OcspStapling        = provider embeds OCSP response in CMS SignedData
    /// </summary>
    public SignatureProviderCapabilities Capabilities =>
        SignatureProviderCapabilities.ServerSideSigning;

    // ── SIGNING ──────────────────────────────────────────────────────────

    public async Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
    {
        // Call vendor API here.
        // Return SignResult.Success(signatureBytes, certInfo) or SignResult.Fail(reason).
        throw new NotImplementedException();
    }

    // ── VERIFICATION ─────────────────────────────────────────────────────

    public async Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken ct = default)
    {
        // Validate CMS SignedData or delegate to CertificateChainValidator.
        // Return VerifyResult.Valid(certInfo, chainResult) or VerifyResult.Invalid(reason).
        throw new NotImplementedException();
    }
}
```

**If server-side signing is NOT supported** (USB token / local agent pattern like VGCA):
- Set `Capabilities = ClientSideSigning | RequiresLocalAgent`
- In `SignAsync`, throw `NotSupportedException("Server-side signing not supported; use local agent.")` — `SignPayloadCommandHandler` will return `409 Conflict` automatically.
- The FE calls the local WSS bridge directly and submits CMS SignedData to `/verify` for audit trail.

---

## Step 2 — Register in InfrastructureRegistration

File: `src/modules/identity/AqtCoreFW.Identity.Infrastructure/InfrastructureRegistration.cs`

Add **both** registrations — keyed (for `Resolve()`) + unkeyed (for `GetAvailableProviders()` enumeration):

```csharp
// Existing registrations:
services.AddKeyedScoped<IDigitalSignatureProvider, UsbTokenSignatureProvider>("usb-vgca");
services.AddScoped<IDigitalSignatureProvider, UsbTokenSignatureProvider>();

services.AddKeyedScoped<IDigitalSignatureProvider, RemoteSigningProvider>("remote-vnpt-smartca");
services.AddScoped<IDigitalSignatureProvider, RemoteSigningProvider>();

// ADD your new provider:
services.AddKeyedScoped<IDigitalSignatureProvider, MyVendorSignatureProvider>("my-vendor-key");
services.AddScoped<IDigitalSignatureProvider, MyVendorSignatureProvider>();

// Add vendor options from appsettings if needed:
services.Configure<MyVendorOptions>(config.GetSection("Signing:MyVendor"));
```

**Why dual registration?**
- `AddKeyedScoped(..., key)` — allows `SignatureProviderResolver.Resolve("my-vendor-key")` via `IServiceProvider.GetKeyedService<T>(key)`.
- `AddScoped<IDigitalSignatureProvider, T>()` (unkeyed) — allows `IServiceProvider.GetServices<IDigitalSignatureProvider>()` to enumerate ALL providers in `GetAvailableProviders()`.

---

## Step 3 — Verify zero-core-change

No modifications needed to:
- `SignatureProviderResolver` — extensible by design (D6)
- `SignPayloadCommandHandler` / `VerifySignatureCommandHandler`
- `SignatureConfigAdminController` / `DigitalSignatureController`
- Any permission code

---

## Step 4 — Add unit tests

Location: `tests/unit/AqtCoreFW.Identity.Infrastructure.Tests/Signing/`

Follow the `ProviderExtensibilityTests` pattern:
1. Register new provider via `BuildResolverWith(("my-vendor-key", new MyVendorSignatureProvider(...)))`.
2. Assert `resolver.Resolve("my-vendor-key")` returns the provider.
3. Assert `resolver.GetAvailableProviders()` contains `ProviderKey = "my-vendor-key"`.
4. Assert `SignAsync` / `VerifyAsync` return expected result shapes.
5. Assert `Capabilities` flags are declared correctly.

---

## Step 5 — Integration test

Location: `tests/integration/AqtCoreFW.Identity.Integration.Tests/Signing/`

1. Seed a `TenantSignatureConfig` row with `ProviderKey = "my-vendor-key"`.
2. GET `/api/v1/admin/security/signature-config/available-providers` → assert new key appears.
3. PUT config with new key → 204; GET → 200 with correct key.
4. POST `/api/v1/digital-signature/sign` → expected status (200 if server-side; 409 if client-side-only).
5. POST `/api/v1/digital-signature/verify` with CMS bytes → 200 with `isValid`.

---

## Step 6 — Update documentation

- `docs/dependency-map.md` — add provider entry under "QĐ 742 Phase-05b" section.
- `docs/compliance/qd-742-cap-4/muc-5-bao-mat/5-1-evidence.md` — add provider to Implementation Reference.
- `docs/compliance/qd-742-cap-4/phase-05b/dossier-section.md` — add vendor brief row.

---

## Checklist

- [ ] `MyVendorSignatureProvider` class created, `ProviderKey` + `Capabilities` declared
- [ ] Keyed + unkeyed DI registration added to `InfrastructureRegistration.cs`
- [ ] No changes to resolver, handlers, or controllers
- [ ] Vendor options (`appsettings`) + secrets (Vault or env var, NOT hardcoded) wired
- [ ] Unit tests: resolve by key, `GetAvailableProviders` enumeration, sign/verify contracts
- [ ] Integration test: available-providers endpoint includes new key
- [ ] `docs/dependency-map.md` updated
- [ ] Architecture tests still pass: `dotnet test --filter Qd742Phase05bSigningComplianceTests`

---

## D6 Literal Scan (CI gate)

The `Compliance_D6_NoHardcodedProviderKeyInDomainAndApplication` / `*InInfrastructureCore` / `*InApiControllerCore` architecture tests scan source files for hard-coded provider key literals (e.g. `"remote-vnpt-smartca"`, `"usb-vgca"`) in core layers.

**Allowed locations** (excluded from scan):
- `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/Signing/**` — provider impls
- `src/modules/identity/AqtCoreFW.Identity.Infrastructure/InfrastructureRegistration.cs` — DI wiring
- `src/host/AqtCoreFW.Api/Controllers/DigitalSignatureController.cs` — user-facing endpoint doc only

**If CI fails** with a literal-scan violation: move the hard-coded string to `InfrastructureRegistration.cs` (as the `AddKeyedScoped` key argument) or to a provider's `ProviderKey` property.
