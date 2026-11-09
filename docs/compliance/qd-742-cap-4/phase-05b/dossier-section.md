# Phase-05b — Digital Signature (§5.1) Dossier Section

**ReqID:** 5.1  
**Mục:** 5 — Bảo mật thông tin liên lạc  
**Status:** MET (shipped 2026-05-11, W1–W9 complete)  
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/phase-05b-digital-signature.md`

---

## Vendor Brief

| Provider Key | Vendor | Type | Capabilities | Status |
|---|---|---|---|---|
| `remote-vnpt-smartca` | VNPT SmartCA | Remote REST API | `ServerSideSigning \| OcspStapling` | Shipped W5 |
| `usb-vgca` | VGCA SignService | USB Token + local WSS | `ClientSideSigning \| RequiresLocalAgent` | Shipped W4 |

*Source specs:* VNPT SmartCA REST API (sp769 PDF public); VGCA USB Token SDK on-disk `other/vgca_usb_token/` (MSI + `vgcaplugin.js` + WSS bridge `wss://127.0.0.1:8987/`).

---

## Wave Delivery Summary

| Wave | Scope | Commit / Branch | Date |
|---|---|---|---|
| W1 | Domain interfaces + records (`IDigitalSignatureProvider`, `ISignatureProviderResolver`, 6 DTOs) | `4392092e` (main) | 2026-05-10 |
| W2 | `TenantSignatureConfig` entity + EF config + SQL migration | main | 2026-05-10 |
| W3 | `VnCaRootStore` + `CertificateChainValidator` (BouncyCastle) | main | 2026-05-10 |
| W4 | `UsbTokenSignatureProvider` (VGCA, verify-only server-side) | main | 2026-05-10 |
| W5 | `RemoteSigningProvider` (VNPT SmartCA, sp769) + WireMock unit tests | main | 2026-05-10 |
| W6 | `SignatureProviderResolver` + 5 CQRS handlers + 2 controllers + permissions | main | 2026-05-10 |
| W7 | FE digital-signature feature (admin config page + sign/verify UI) | main | 2026-05-10 |
| W8 | D6 acceptance gate: `ProviderExtensibilityTests`, `Qd742Phase05bSigningComplianceTests` (18 facts), E2E API tests (28 HTTP boundary tests) | `cccec2df` (main) | 2026-05-10 |
| W9 | Compliance docs: evidence file, runbook, dossier (this file), checklist row 37 = PARTIAL until NEAC root seed (40 MET + 1 PARTIAL = 97.6%) | main | 2026-05-11 |

---

## Architecture (D6 Extensibility)

```
IDigitalSignatureProvider (domain interface)
├── UsbTokenSignatureProvider     key: "usb-vgca"          [ClientSideSigning | RequiresLocalAgent]
└── RemoteSigningProvider         key: "remote-vnpt-smartca" [ServerSideSigning | OcspStapling]

SignatureProviderResolver
├── Resolve(key)          → sp.GetKeyedService<IDigitalSignatureProvider>(key)
└── GetAvailableProviders → IEnumerable<IDigitalSignatureProvider> (unkeyed DI enumeration)

InfrastructureRegistration
├── AddKeyedScoped<..., UsbTokenSignatureProvider>("usb-vgca")
├── AddScoped<..., UsbTokenSignatureProvider>()            ← unkeyed for enumeration
├── AddKeyedScoped<..., RemoteSigningProvider>("remote-vnpt-smartca")
└── AddScoped<..., RemoteSigningProvider>()                ← unkeyed for enumeration
```

D6 mandate: new provider = 1 class + 2 DI lines. Zero changes to resolver, handlers, controllers.

---

## Key Design Decisions

| Decision | Rationale |
|---|---|
| `IEnumerable<IDigitalSignatureProvider>` constructor injection in `SignatureProviderResolver` | Enables `GetAvailableProviders()` without static key list — D6 zero-core-change extensibility |
| Dual DI registration (keyed + unkeyed) | Keyed for key-based `Resolve()`; unkeyed for `IEnumerable<>` enumeration |
| `ClientSideSigning | RequiresLocalAgent` for VGCA | USB WSS bridge runs locally; server cannot call it — FE handles sign, submits CMS to `/verify` |
| D17.2 source literal scan (file walk, not Roslyn) | No new package dependency; same pattern as `CrossSchemaSqlScanner`; inline comment stripping prevents false positives |
| OCSP: `Soft` default (warn if unreachable) vs `Hard` (reject if unreachable) | Per-tenant override via `TenantSignatureConfig.OcspMode`; `Hard` for high-assurance tenants |
| No self-built PKI | QĐ 742 §5.1 requires accredited VN CA only; in-process signing removed 2026-05-01 |

---

## Amendments Applied

| Amendment | Summary |
|---|---|
| D6 | New VN CA provider = zero core change. `GetAvailableProviders()` from DI, not static key array. |
| D17.2 | D6 CI gate = source literal scan (file walk). Banned literals: `"remote-vnpt"`, `"usb-vgca"`, `"fake-thirdparty"` in Domain/Application/InfrastructureCore/ApiControllerCore layers. |

---

## Test Evidence Summary

| Suite | Test Class | Count | Result |
|---|---|---|---|
| Architecture | `Qd742Phase05bSigningComplianceTests` | 18 facts | PASS |
| Unit | `ProviderExtensibilityTests` | 6 facts | PASS |
| Unit | `SignatureProviderResolverTests` | (existing, updated) | PASS |
| Integration | `SignatureConfigApiTests` | 15 HTTP boundary | PASS |
| Integration | `DigitalSignatureApiTests` | 13 HTTP boundary | PASS |
| **Total** | | **52+** | **ALL PASS** |

Run commands:
```bash
dotnet test --filter "Qd742Phase05bSigningComplianceTests"          # 18/18
dotnet test --filter "ProviderExtensibilityTests"                   # 6/6
dotnet test --filter "SignatureConfigApiTests|DigitalSignatureApiTests"  # 28/28
```

---

## Compliance Statement

QĐ 742 §5.1 requires: *"Mã hóa dữ liệu khi truyền qua mạng sử dụng chữ ký số"* (for applications that require digital signatures).

AqtCoreFW satisfies this via:
1. Integration with accredited Vietnamese CA services (VNPT SmartCA + VGCA) — no self-built PKI.
2. CMS SignedData format (PKCS#7) for all signatures — industry standard for signed data over network.
3. OCSP validation (Soft/Hard configurable per tenant) against VN CA trust anchors.
4. Per-tenant admin-configurable provider selection with MFA + RBAC gating on all admin operations.
5. Audit trail on all sign/verify operations via `AuditBehavior` pipeline.
6. D6 extensibility: future VN CA integrations require zero core changes.

Evidence file: [`muc-5-bao-mat/5-1-evidence.md`](../muc-5-bao-mat/5-1-evidence.md)  
Add-provider runbook: [`phase-05b/add-new-provider.md`](add-new-provider.md)
