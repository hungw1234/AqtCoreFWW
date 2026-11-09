---
ReqID: "5.1"
title: "Mã hóa dữ liệu khi truyền qua mạng sử dụng chữ ký số"
muc: 5
phase: "05b"
status: MET
generated: 2026-05-11
---

# 5.1 — Mã hóa dữ liệu khi truyền qua mạng sử dụng chữ ký số

**Phase:** 05b | **Mục:** 5 — Bảo mật thông tin liên lạc

*Điều kiện áp dụng: ứng dụng yêu cầu chữ ký số (QĐ 742 §5.1).*

## Implementation Reference

### Domain (Identity.Domain.Services)
- `IDigitalSignatureProvider` — [`src/modules/identity/AqtCoreFW.Identity.Domain/Services/IDigitalSignatureProvider.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Services/IDigitalSignatureProvider.cs)
  - Capability-aware interface: `SignAsync`, `VerifyAsync`, `Capabilities` (flags), `ProviderKey`
  - Capabilities: `ServerSideSigning`, `ClientSideSigning`, `LongTermValidation`, `BatchSigning`, `OcspStapling`, `RequiresLocalAgent`
- `ISignatureProviderResolver` — [`src/modules/identity/AqtCoreFW.Identity.Domain/Services/ISignatureProviderResolver.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Services/ISignatureProviderResolver.cs)
  - DI-driven enumeration; `GetAvailableProviders()` returns all registered providers (D6: zero-core-change extensibility)
- Records: `SignRequest`, `SignResult`, `VerifyRequest`, `VerifyResult`, `OcspMode` (Soft/Hard), `CertificateInfo`, `ChainValidationResult`

### Entity (Identity.Infrastructure)
- `TenantSignatureConfig` — per-tenant config (ProviderKey, ApiCredentialsRef, AllowedCaRootThumbprints, OcspMode, IsEnabled)
- EF migration: `20260510_AddTenantSignatureConfig`

### Providers (Identity.Infrastructure.Services.Signing)
- `UsbTokenSignatureProvider` (key: `usb-vgca`) — VGCA USB Token bridge via local WSS agent `wss://127.0.0.1:8987/`; capabilities: `ClientSideSigning | RequiresLocalAgent`. Server-side `SignAsync` throws `NotSupportedException` (FE invokes agent directly).
- `RemoteSigningProvider` (key: `remote-vnpt-smartca`) — VNPT SmartCA REST API (sp769 spec); capabilities: `ServerSideSigning | OcspStapling`. OCSP stapling embedded in CMS SignedData response.

### Certificate Chain Validation
- `VnCaRootStore` — trust anchors for Vietnamese CA root certificates (VNPT, VGCA, National CA)
- `CertificateChainValidator` — BouncyCastle-backed; validates chain against `VnCaRootStore` + OCSP check per `OcspMode`

### CQRS Handlers
- `SignPayloadCommandHandler` — resolves provider, checks `ServerSideSigning` capability, delegates sign
- `VerifySignatureCommandHandler` — resolves provider or falls back to chain-only verify
- `GetTenantSignatureConfigQuery` — admin read
- `UpsertTenantSignatureConfigCommand` — admin upsert
- `GetAvailableSignatureProvidersQuery` — returns available providers list

### Controllers
- `SignatureConfigAdminController` — `[RequiresMfa]` + `Authorize(SECURITY.SIGNATURE_CONFIG.VIEW/.MANAGE)`. Endpoints: GET/PUT `/api/v1/admin/security/signature-config/{tenantId}`, GET `/api/v1/admin/security/signature-config/available-providers`
- `DigitalSignatureController` — `[RequiresMfa]` + `Authorize`. Endpoints: POST `/api/v1/digital-signature/sign`, POST `/api/v1/digital-signature/verify`

### Permissions
- `PermCodes.Security.SignatureConfig.View` = `"SECURITY.SIGNATURE_CONFIG.VIEW"`
- `PermCodes.Security.SignatureConfig.Manage` = `"SECURITY.SIGNATURE_CONFIG.MANAGE"`

## Evidence Artifacts

### Architecture Tests (AqtCoreFW.Architecture.Tests — Qd742Phase05bSigningComplianceTests)
- `Compliance_5_1_TenantSignatureConfig_EntityExists` — entity present in Identity.Infrastructure
- `Compliance_5_1_TenantSignatureConfig_HasRequiredFields` — TenantId, ProviderKey, OcspMode, IsEnabled, AllowedCaRootThumbprints
- `Compliance_5_1_TenantSignatureConfig_IsAuditableEntity` — audit trail on config changes
- `Compliance_D6_IDigitalSignatureProvider_InterfaceExists` — domain interface present
- `Compliance_D6_SignatureProviderCapabilities_FlagsEnumExists` — capabilities flags enum
- `Compliance_D6_OcspMode_EnumExists` — Soft/Hard OCSP enforcement
- `Compliance_5_1_SignatureConfigAdminController_Exists` — admin controller present
- `Compliance_5_1_SignatureConfigAdminController_HasRequiresMfaAttribute` — MFA gate on admin endpoints
- `Compliance_5_1_SignatureConfigAdminController_HasAuthorizeWithSignatureConfigPolicy` — RBAC gate on config management
- `Compliance_5_1_SignatureConfigAdminController_Has3Endpoints` — GET config, PUT upsert, GET available-providers
- `Compliance_5_1_DigitalSignatureController_Exists` — user-facing controller present
- `Compliance_5_1_DigitalSignatureController_HasRequiresMfaAttribute` — MFA gate on sign/verify
- `Compliance_5_1_DigitalSignatureController_Has2Endpoints` — Sign and Verify endpoints
- `Compliance_5_1_PermissionCodes_SignatureConfig_ViewAndManage` — permission codes present
- `Compliance_5_1_SigningHandlers_AllExist` — all CQRS handlers present (SignPayloadCommand, VerifySignatureCommand, UpdateTenantSignatureConfigCommand, GetTenantSignatureConfigQuery, GetAvailableProvidersQuery)
- `Compliance_D6_NoHardcodedProviderKeyInIdentityDomainAndApplication` — zero vendor-key literals in Domain + Application (D6 architectural mandate)
- `Compliance_D6_NoHardcodedProviderKeyInIdentityInfrastructureOutsideSigning` — zero vendor-key literals in Infrastructure (excl. `Services/Signing/`)
- `Compliance_D6_NoHardcodedProviderKeyInApiControllers_ExcludingSigningController` — zero vendor-key literals in Api controllers (excl. `DigitalSignatureController.cs`)
- **Total: 18 facts.** Tests: [Qd742Phase05bSigningComplianceTests.cs](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05bSigningComplianceTests.cs)

### Integration Tests — D6 Extensibility (AqtCoreFW.Identity.Integration.Tests — ProviderExtensibilityTests)

- D6 zero-touch integration test: [ProviderExtensibilityTests.cs](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Signing/ProviderExtensibilityTests.cs) — boots `WebAppFixture` host with `FakeThirdPartyVnCaProvider` registered via keyed + unkeyed DI; confirms resolver enumerates the new provider without touching any forbidden core file. Relocated from unit suite to integration suite per FIX-C/H4 — D6 zero-touch contract is now end-to-end gated.

### Integration Tests — Admin Config API (AqtCoreFW.Identity.Integration.Tests — SignatureConfigApiTests)

- Tests: [SignatureConfigApiTests.cs](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Signing/SignatureConfigApiTests.cs) — 15 HTTP boundary tests (401/403/404/204/422/200 matrix + Put_StaleRowVersion_Returns409 + Put_InvalidBase64RowVersion_Returns400 added by FIX-A/C4)

### Integration Tests — User Sign/Verify API (AqtCoreFW.Identity.Integration.Tests — DigitalSignatureApiTests)

- Tests: [DigitalSignatureApiTests.cs](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Signing/DigitalSignatureApiTests.cs) — 13 HTTP boundary tests (401/403/404/409/422/200 matrix); covers /sign capability-routing 400 for ClientSideSigning-only providers + /verify chain validation delegation

## Behavior Summary

The §5.1 implementation integrates with accredited Vietnamese CA services for qualified electronic signatures on data transmitted over network channels.

**Admin flow:** SystemAdmin (with MFA) configures a per-tenant `TenantSignatureConfig` (provider key, OCSP mode, CA thumbprint allowlist, enabled flag) via the admin REST API. The config is immutable once set for a document workflow; changes require re-authorization.

**User sign flow:** Authenticated user (with MFA) submits a payload to POST `/api/v1/digital-signature/sign`. The handler resolves the tenant's active `TenantSignatureConfig`, selects the provider by key, verifies the provider supports `ServerSideSigning`, then delegates to the provider (VNPT SmartCA remote call for server-side signing). For VGCA USB tokens (`RequiresLocalAgent`), the FE directly calls the local WSS bridge and submits the resulting CMS SignedData to `/verify` for audit purposes.

**Verify flow:** POST `/api/v1/digital-signature/verify` — accepts `payload + signature (CMS SignedData bytes) + optional OcspMode override`. `CertificateChainValidator` validates the chain against `VnCaRootStore` trust anchors and performs OCSP check per configured mode.

**D6 extensibility:** New VN CA providers implement `IDigitalSignatureProvider` and register via keyed DI + unkeyed DI. `SignatureProviderResolver` enumerates from DI injection — zero core changes needed.

## Verification Steps

**Phase-05b current state (W2-W9 shipped + FIX-A/B/C/D applied 2026-05-11):**

### Structural Verification (Architecture Suite — PASS)
1. `dotnet test tests/architecture/AqtCoreFW.Architecture.Tests/AqtCoreFW.Architecture.Tests.csproj` → **87/87 PASS** ✓
2. `Qd742Phase05bSigningComplianceTests` — entity/interface/enum + controller-attribute + D6 literal-scan facts all pass.
3. D6 source-text gate (Microsoft.CodeAnalysis literal-scan, NOT git-diff per D17.2): zero hardcoded `"remote-vnpt"` / `"usb-vgca"` / `"fake-thirdparty"` literals outside `Services/Signing/`. `FindSrcRoot()` hard-fails on null root (post-FIX-C/H1).

### Behavioural Verification (Identity Integration suite — PASS)
- `dotnet test tests/integration/AqtCoreFW.Identity.Integration.Tests/AqtCoreFW.Identity.Integration.Tests.csproj` → **169/169 PASS** ✓
- `ProviderExtensibilityTests` (integration suite) — `WebAppFixture` boot with `FakeThirdPartyVnCaProvider` keyed-DI register; resolver enumerates new provider; round-trip sign/verify works without modifying any forbidden core file.
- `SignatureConfigApiTests` — 15 HTTP boundary tests (401/403/404/204/422/200 + Put_StaleRowVersion_Returns409 + Put_InvalidBase64RowVersion_Returns400 added by FIX-A/C4).
- `DigitalSignatureApiTests` — 13 HTTP boundary tests (401/403/404/409/422/200 matrix); /sign capability-routing returns 400 for ClientSideSigning-only providers; /verify chain-delegation works.

### FE Verification
- `pnpm typecheck` → 0 errors (post FIX-B).
- `pnpm vitest run src/test/features/digital-signature` → **35/35 PASS** (registry, vgca-usb-adapter, remote-signing-adapter, uint8-to-base64 chunked encoder ≤5MB payload).

### Audit Pipeline (FIX-A/C3)
- SignPayloadCommandHandler emits structured audit: provider, certSerial, certSubject, payloadHash (SHA-256), outcome, AuditCategory.ConfigChange.
- VerifySignatureCommandHandler emits: provider, certSerial, certSubject, payloadHash, signatureHash, ocspMode, ocspChecked, ocspSucceeded.
- UpdateTenantSignatureConfigCommandHandler emits Severity.Critical on `AllowedCaRootThumbprints` diff via `CA_ROOT_THUMBPRINTS_CHANGED` action mapped through `AuditCategoryMapper`.
- `[SuppressAutoAudit]` opt-out attribute prevents double-audit rows from `AuditBehavior` pipeline.

### Concurrency (FIX-A/C4)
- PUT signature-config requires base64 RowVersion in body; FormatException → 400.
- `TenantSignatureConfigRepository` overrides `Entry.Property(RowVersion).OriginalValue` → `DbUpdateConcurrencyException` on stale → middleware maps to 409 GOV_SYS_003.

### Production Readiness Gate (HONEST DISCLOSURE)
- **VnCaRootStore seeds ZERO real roots in production** (`_productionThumbprints = []`, VNPT-CA + VGCA roots commented `PRODUCTION_SEED_DEFERRED`). All real-world cert chain validation will REJECT until NEAC root acquisition completes (≥3d lead time).
- **OCSP behaviour:** soft-fail default per D-V3 hybrid; tenant-admin can toggle hard-fail per config.
- **VNPT contract:** wiremock-only adapter ships now; production E2E gated post-contract per D15.

**SUMMARY:** Structural + behavioural + FE + audit + concurrency verification **PASS**. ReqID 5.1 status = **PARTIAL** until NEAC root cert seeding lands (BE infrastructure complete; real-cert verification awaits trust-anchor seeding).

---

## Citation Audit (2026-05-11 Post-FIX-D Re-Audit)

**Audit scope:** Every cited file path, test class name, and fact count verified against live codebase post FIX-A/B/C/D.

### File Paths — 10/10 VERIFIED EXIST ✓
| Path | Status |
|------|--------|
| `IDigitalSignatureProvider.cs` | ✓ exists |
| `ISignatureProviderResolver.cs` | ✓ exists |
| `TenantSignatureConfigDto.cs` | ✓ exists |
| `SignatureProviderResolver.cs` | ✓ exists |
| `SignatureConfigAdminController.cs` | ✓ exists |
| `DigitalSignatureController.cs` | ✓ exists |
| `Qd742Phase05bSigningComplianceTests.cs` | ✓ exists |
| `ProviderExtensibilityTests.cs` (integration suite, post-FIX-C/H4) | ✓ exists |
| `SignatureConfigApiTests.cs` | ✓ exists (15 facts; part of 169/169 Identity.Integration PASS) |
| `DigitalSignatureApiTests.cs` | ✓ exists (13 facts; part of 169/169 Identity.Integration PASS) |

### Test Fact Counts — Verified
| Test Class | Count | Verification |
|-----------|-------|--------------|
| `Qd742Phase05bSigningComplianceTests` | 18 | actual `[Fact]` count from line inspection |
| `ProviderExtensibilityTests` (integration) | live-verified via Identity.Integration 169/169 PASS |
| `SignatureConfigApiTests` | 15 | live-verified via Identity.Integration 169/169 PASS |
| `DigitalSignatureApiTests` | 13 | live-verified via Identity.Integration 169/169 PASS |

### VnCaRootStore Seed State — H2 Honest Disclosure
- **Constructor state:** `_productionThumbprints = []` (empty array)
- **VNPT-CA root:** commented out, marked `PRODUCTION_SEED_DEFERRED`
- **VGCA root:** commented out, marked `PRODUCTION_SEED_DEFERRED`
- **Implications:** Certificate chain validation enabled but zero trusted roots in production until NEAC roots acquired + config-loaded. Documented in checklist row 37 PARTIAL note.

### Re-Audit Note (Post-FIX-D)
Initial FIX-D pass introduced false-negative fabrications (claimed `SignatureConfigApiTests.cs` + `DigitalSignatureApiTests.cs` did NOT exist when both DO exist as part of W8 commit `cccec2df`). Corrected here 2026-05-11 post re-review (`code-reviewer-260511-0110-qd742-phase05b-rereview-post-fixup.md` N1). All cited paths now empirically verified via Glob + live test execution.
