# Signing Spec Amendments — QĐ 742 Mục 5.1

This file tracks post-planning spec amendments to the digital signature integration (Phase-05b and successors).
Full D1-D16 history: `plans/260428-1740-cap-4-qd-742-compliance/amendments-log.md`.

---

## D17 — Phase-05b Pre-Wave-2 Spec Reconciliation (2026-05-10)

Summary: W1-shipped surface aligned to spec. Key changes:
- `Resolve(Guid tenantId)` → `Resolve(string providerKey)` (SoC: resolver = pure DI lookup).
- VnCaRootStore scope tightened to VNPT-CA + VGCA only (YAGNI — MiSA/Viettel not in vendor matrix at time).

Full detail: `plans/260428-1740-cap-4-qd-742-compliance/amendments-log.md` § D17.

---

## D18 — Phase-05b Post-Ship Fix-Up Wave (2026-05-11)

Summary: post-ship audit fixes for W1-W9. RowVersion concurrency + DTO alignment + test gates.

Full detail: `plans/260428-1740-cap-4-qd-742-compliance/amendments-log.md` § D18.

---

## D19 — Contract refactor for full tier coverage + Viettel pivot (2026-05-11)

### Rationale

Phase-05b W1-W9 shipped 2026-05-11 with mock-only providers. Post-ship review against on-disk VGCA SDK
(`other/vgca_usb_token/`) and extracted Viettel Mysign API reference
(`plans/reports/viettel-mysign-extracted-260511.txt`) revealed:

1. **VGCA `vgca_sign_msg` returns raw RSA/ECDSA bytes + separate `CertBase64`** — NOT CMS/PKCS#7.
   The existing `UsbTokenSignatureProvider.VerifyAsync` which parses the signature as `CmsSignedData`
   is fundamentally wrong for Tier-1 raw mode.
2. **VGCA SDK exposes 3 signing tiers** — the MVP scope must cover all three:
   - Tier-1: raw hash-based sign (`vgca_sign_msg`)
   - Tier-2: XML/JSON structured document signing (`vgca_sign_xml`, `vgca_sign_json`)
   - Tier-3: PDF doc-flow with `FileUploadHandler` round-trip (7 VGCA plugin functions)
3. **Vendor lock pivot**: VNPT SmartCA sp769 → Viettel Mysign (3-mode: sync / async-poll / async-callback).
   Viettel Mysign API reference (v1.9, 100 pages) confirms base surface: login, `certificates/info`,
   `signHash` — single provider, mode determined by API request parameters.

### Changes

| Area | Before D19 | After D19 |
|---|---|---|
| `SignatureProviderCapabilities` enum | 6 flags (None…RequiresLocalAgent=32) | +4: RawSign=64, XmlDsig=128, JsonSign=256, DocFlow=512 |
| `SignRequest` | 4 fields | +3 nullable: HashAlg, Encode, RequestedFormat (named defaults, backward-compat) |
| `VerifyRequest` | 3 fields | +4: SignerCertDer (Tier-1 raw), HashAlg="SHA256", Encode="RSA", Format=Pkcs7 (backward-compat default) |
| `SignatureFormat` enum | — (new) | Raw=0, Pkcs7=1, XmlDsig=2, Jws=3 |
| `IStructuredSignatureService` | — (new) | Tier-2 verify interface (impl W2) |
| `IDocumentSigningWorkflow` | — (new) | Tier-3 doc-flow interface, 7 methods 1-to-1 with VGCA plugin functions (impl W3) |
| `ISignatureProviderResolver` | 1-arg `Resolve(key)` | +overload `Resolve(key, required)` capability-gated (impl W1) |
| `TenantSignatureConfig.ProviderKey` | accepts any non-empty string ≤64 chars | deprecated VNPT SmartCA key rejected at Application validator layer (D6-safe) |

### File changes (W0)

- **Modified**: `IDigitalSignatureProvider.cs` — expanded enum + records + `SignResult.IsPending` Viettel async doc
- **Modified**: `ISignatureProviderResolver.cs` — added capability-gated overload
- **Modified**: `TenantSignatureConfig.cs` — XML doc deprecation notice + updated ProviderKey doc
- **Created**: `SignatureFormat.cs`
- **Created**: `IStructuredSignatureService.cs`
- **Created**: `IDocumentSigningWorkflow.cs`
- **Created**: `docs/signing/spec-amendments.md` (this file)

### Deprecation timeline — VNPT SmartCA provider key

| Date | Event |
|---|---|
| 2026-05-11 | D19 lands. New writes of deprecated VNPT SmartCA key rejected at Application validator (`UpdateTenantSignatureConfigCommandValidator`). |
| W4 ship date | Viettel Mysign provider (`remote-viettel-mysign`) fully implemented. Admin UI can migrate tenant configs. |
| Post-W4 cleanup | Rename `RemoteSigningProvider` → `ViettelMysignProvider`; remove sp769 HTTP client code. |
| Future | Hard cleanup pass removes deprecated key from `InfrastructureRegistration.cs` DI keyed registration. |

Existing tenant rows with the deprecated key remain **readable** and the signing endpoints continue to
function (provider is still registered in DI) until the admin explicitly reconfigures the tenant.
The hard reject is intentionally at the Application layer (not Domain entity) to preserve the D6
architecture guard (no hardcoded vendor strings in `Identity.Domain` or `Identity.Application` source).

### Backward compatibility

- Existing 169 Identity.Integration tests + 87 architecture tests GREEN after W0 (contract-only changes).
- Existing positional ctor for `SignRequest` (4 args) and `VerifyRequest` (3 args) still compile — new fields at end with named defaults.
- `Resolve(string)` 1-arg signature unchanged.
- `SignResult` record unchanged.

### Viettel Mysign async status codes (SignResult.IsPending)

| Status | Meaning | `IsPending` | `IsSuccess` |
|---|---|---|---|
| 4000 | Pending user confirmation on mobile app | true | false |
| 6000 | Signing in progress (user confirmed, CA processing) | true | false |
| 2000 | Completed | false | true |
| Other | Error | false | false |
