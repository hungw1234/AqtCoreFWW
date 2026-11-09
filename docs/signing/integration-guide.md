# AqtCoreFW Signing Integration Guide

**Last updated:** 2026-05-11 | **Status:** Production-ready (except NEAC root cert seeding)

This guide documents the complete API contract, architecture, and integration patterns for the AqtCoreFW digital signature system (Phase-05b W0-W9 + QĐ 742 mục 5.1).

---

## 1. Architecture Overview

AqtCoreFW supports two distinct provider ecosystems:

### 1.1 VGCA USB Token (4-layer FE→Plugin→Desktop→USB)

```
┌─────────────┐
│  Browser    │ FE calls window.vgca_sign_msg({ payload, opts })
│    (React)  │
└──────┬──────┘
       │ window.postMessage({handler, payload, ...})
       ↓
┌──────────────────────────┐
│  vgcaplugin.js (browser) │ Injects vgca_sign_* functions
│  on-disk: other/vgca     │ 
└──────┬───────────────────┘
       │ ws:// localhost:8987
       ↓
┌──────────────────────────┐
│ VGCASignService (desktop)│ Native Win/Mac/Linux app
│ (MSI installer)          │ via sp769 spec + SDK
└──────┬───────────────────┘
       │ USB APDU
       ↓
┌──────────────────────────┐
│    USB Token (Hardware)  │ RSA 2048 or ECDSA P-256
│   (SafeNet, Feitian)     │
└──────────────────────────┘
```

**BE endpoint for verification:** `POST /api/v1/digital-signature/verify`

---

### 1.2 Viettel Mysign (3-mode Mobile Authorisation)

```
┌─────────────────────────────────────────────┐
│          AqtCoreFW Backend (BE)             │
│  POST /api/v1/digital-signature/sign       │
│  (payload, provider=remote-viettel-mysign) │
└──────┬──────────────────────────────────────┘
       │
       ├─ Sync (async=0)      → /signHash blocks 120s
       │                         User confirms PIN on phone
       │                         Returns {signatures:[]}
       │
       ├─ Async-Poll (async=2) → /signHash returns txId
       │                         BE polls /requests/status every 3s
       │                         Phone app confirms in background
       │
       └─ Async-Callback (async=3) → /signHash returns txId
                                      Mysign webhook → POST /api/signing/mysign-callback/result
                                      BE stores pending tx → FE polls confirm endpoint
```

**BE endpoints:**
- `POST /api/v1/digital-signature/sign` — initiate sign (all modes)
- `POST /api/v1/digital-signature/confirm` — confirm async result (modes 2+3)
- `POST /api/signing/mysign-callback/auth` — Mysign credential auth (mode 3)
- `POST /api/signing/mysign-callback/result` — Mysign result callback (mode 3)

---

## 2. Provider Capability Matrix

| Feature | VGCA USB | Viettel Mysign | Notes |
|---------|----------|---|---|
| **ProviderKey** | `usb-vgca` | `remote-viettel-mysign` | Used in TenantSignatureConfig |
| **Tier-1 Raw Sign** | ✓ (RawSign=64) | ✓ (RawSign=64) | Hash-based signature; cert separate |
| **Tier-2 XML/JWS** | ✓ (XmlDsig=128, JsonSign=256) | ✗ | VGCA only via `vgca_sign_xml`/`vgca_sign_json` |
| **Tier-3 Doc-Flow** | ✓ (DocFlow=512) | ✗ | VGCA only via 7 VGCA plugin functions |
| **Client-Side Signing** | ✓ (ClientSideSigning=2) | ✗ | VGCA plugin runs on FE; MSI required |
| **Server-Side Signing** | ✗ | ✓ (ServerSideSigning=1) | Mysign API authorises on mobile |
| **Batch Signing** | — (per-doc in tier-3) | ✓ (BatchSigning=4) | Viettel sign multiple in 1 call |
| **Requires Local Agent** | ✓ (RequiresLocalAgent=32) | ✗ | VGCA MSI must be installed |
| **OCSP Validation** | ✓ (LongTermValidation=8) | ✓ (LongTermValidation=8) | Chain verified post-sign |

---

## 3. Provider Selection Rubric

**Use VGCA USB token when:**
- Document must carry visible stamp (tiêu chí phê duyệt, ký công văn)
- Offline signing capability required
- User has physical USB token + MSI installed
- Integrating with VN government e-workflow systems

**Use Viettel Mysign when:**
- Server-side mobile authorisation preferred
- No local agent installation required
- Batch signing required (multiple docs at once)
- Certificate lifecycle managed by Viettel

**Per-tenant routing:** `TenantSignatureConfig.ProviderKey` specifies which provider to use for a tenant. Admin can override via `POST /api/v1/admin/signing/tenant-config` (requires `SECURITY.SIGNING.MANAGE`).

---

## 4. Tier-1 VGCA Raw-Hash Signing (W1)

**Use case:** Sign a hash without embedded document structure (audit logs, raw approvals).

### 4.1 FE Adapter API

```javascript
// From: web/src/features/digital-signature/providers/vgca-usb-adapter.ts
const result = await window.vgca_sign_msg({
  payload: new Uint8Array([/* SHA256 hash bytes */]),
  hashAlg: "SHA256",        // or SHA384, SHA512
  encode: "RSA",            // or "EC"
  reason: "Approve contract" // optional
});

// Returns:
// {
//   status: 0,              // 0=success, 500=websocket fail, 4002=user cancel
//   signature: "base64(...)",// raw RSA/ECDSA bytes (270-512 bytes for RSA2048)
//   certBase64: "MIIF...",  // X.509 leaf cert DER
//   keyInfo: { algorithm: "RSA", keySize: 2048 }
// }
```

### 4.2 BE Verify Pipeline

**POST** `/api/v1/digital-signature/verify`

```json
{
  "payload": "base64(SHA256_hash_bytes)",
  "signature": "base64(raw_rsa_signature_bytes)",
  "signerCertBase64": "MIIF...",
  "hashAlg": "SHA256",
  "encode": "RSA",
  "format": "Raw"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "isValid": true,
    "signingCertificate": {
      "serialNumber": "01:2A:3B:4C:...",
      "subject": "CN=User Name,O=VN Company",
      "issuer": "CN=VGCA Sub-CA,O=Vietnam",
      "notBefore": "2024-01-01T00:00:00Z",
      "notAfter": "2026-01-01T00:00:00Z",
      "thumbprint": "AB12CD34..."
    },
    "chainResult": {
      "isValid": true,
      "ocspChecked": true,
      "ocspSucceeded": true
    }
  }
}
```

### 4.3 Error Codes

| VGCA Status | HTTP | Meaning | Recovery |
|-------------|------|---------|----------|
| 0 | 200 | Success | — |
| 500 | 502 | WebSocket to desktop service failed | Retry; ensure MSI running |
| 503 | 503 | VGCA SignService unavailable | Restart desktop app |
| 4002 | 400 | User clicked cancel | Ask user to retry |
| Other | 400 | Plugin internal error | Log + escalate to VGCA support |

---

## 5. Tier-2 VGCA Structured Document Signing (W2)

### 5.1 XML Document Signing (XMLDSig)

**FE adapter call:**
```javascript
const result = await window.vgca_sign_xml({
  xmlBase64Content: "PD94bWw...",
  hashAlg: "SHA256",
  xmlDsigForm: "Detached"  // or "Enveloped"/"Enveloping"
});
// Returns { status, signature, certBase64, ... }
```

**BE verification:**

```csharp
// Endpoint: POST /api/v1/digital-signature/verify-xml
var request = new VerifyRequest(
  Payload: Encoding.UTF8.GetBytes(xmlContent),  // original XML bytes
  Signature: Convert.FromBase64String(signedXmlDsigBase64),
  OcspMode: OcspMode.Soft
);

var result = await structuredSignatureService.VerifyXmlDsigAsync(
  signedXmlDsigBase64,
  OcspMode.Soft,
  cancellationToken
);
```

**Key considerations:**
- C14N (Canonical XML) assumed for namespace normalization
- XmlResolver = null (XXE prevention)
- Cert extracted from `KeyInfo/X509Data` element inside signed document
- Subject must match expected OU (e.g., "CN=Approver,OU=Finance")

### 5.2 JSON Signing (JWS)

**FE adapter call:**
```javascript
const result = await window.vgca_sign_json({
  jsonBase64Content: "eyJkb...",
  hashAlg: "SHA256"
});
// Returns JWS compact serialization: header.payload.signature
```

**BE verification:**

```csharp
var result = await structuredSignatureService.VerifyJwsAsync(
  jwsCompactString,  // "eyJ...eyJ...ey..."
  OcspMode.Hard,
  cancellationToken
);
```

**JWS header format:**
```json
{
  "alg": "RS256",
  "typ": "JWT",
  "x5c": ["MIIF...", "MIIF..."],  // leaf + intermediates
  "kid": "2026-05-01"
}
```

**Rejected algorithms:** `alg=none` (CVE-2015-9235). Validation explicitly rejects unsigned JWS.

---

## 6. Tier-3 VGCA Document-Flow Signing (W3)

**Use case:** Sign a Vietnamese government document (công văn, hợp đồng) with visible stamp containing signer info.

### 6.1 7 Document-Flow Methods

| Method | VGCA Function | Workflow Type | Description |
|--------|---|---|---|
| SignIssuedAsync | vgca_sign_issued | Phát hành | Issue document (official letterhead) |
| SignApprovedAsync | vgca_sign_approved | Phê duyệt | Leadership approval (giám đốc ký) |
| SignIncomeAsync | vgca_sign_income | Ký công văn đến | Incoming doc receipt |
| CommentAsync | vgca_comment | Góp ý | Comment/annotation |
| SignAppendixAsync | vgca_sign_appendix | Phụ lục | Attachment signature |
| SignCopyAsync | vgca_sign_copy | Bản sao | Electronic copy cert |
| SignFilesAsync | vgca_sign_files | Ký hàng loạt | Batch sign multiple |

### 6.2 Initiation Flow (POST /api/signing/docflow/initiate)

**FE Request:**
```json
{
  "docId": "550e8400-e29b-41d4-a716-446655440000",
  "signType": "Issued",
  "originalStorageKey": "docs/contract-001.pdf",
  "docNumber": "CV/2026/001",
  "issuedDate": "2026-05-11T00:00:00Z",
  "fileName": "contract-signed.pdf",
  "extra": {
    "department": "Legal",
    "approver": "Director"
  }
}
```

**BE Response:**
```json
{
  "success": true,
  "data": {
    "handlerUrl": "https://api.example.com/api/signing/file",
    "jwt": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresIn": 300
  }
}
```

### 6.3 JWT Lifecycle

- **TTL:** 5 minutes (300s) from issuance
- **Nonce tracking:** 2 uses per token (1 download + 1 upload)
- **Signing key:** Vault `aqt/encryption/docflow-jwt-sign` (managed by Phase-06 key rotation)
- **Claims:** docId, tenantId, userId, signType, iat, exp

### 6.4 File Handler Endpoints

**GET** `/api/signing/file?token=<jwt>`

VGCA SignService calls this to download PDF. Returns raw PDF bytes + Content-Disposition header.

**POST** `/api/signing/file`

VGCA uploads signed PDF as multipart form:
```
Content-Type: multipart/form-data
token: <jwt>
uploadfile: <binary PDF>
metadata: {
  "documentId": "550e8400-e29b-41d4-a716-446655440000",
  "resultCode": "0",
  "signatureInfo": { "serialNumber": "01:2A:...", "timestamp": "2026-05-11T12:34:56Z" }
}
```

**BE storage:** Signed PDF persists to `IFileStorageService` bucket "signing-docflow-result" with key returned in response.

---

## 7. Viettel Mysign — Sync Mode (async=0)

**Use case:** Single-sign approval; user confirms on phone while BE blocks (up to 120s).

### 7.1 Sign Request

**POST** `/api/v1/digital-signature/sign`

```json
{
  "payload": "base64(hash_bytes)",
  "userId": "user123@example.com",
  "reason": "Approve RFP #2026-001",
  "extraParameters": {
    "provider": "remote-viettel-mysign",
    "async": "0",
    "hashAlgorithm": "SHA256",
    "signatureAlgorithm": "RSA"
  },
  "hashAlg": "SHA256",
  "encode": "RSA",
  "requestedFormat": "Raw"
}
```

### 7.2 Viettel API Calls (BE-side)

**1. Login → Bearer token**

```http
POST https://remotesigning.viettel.vn/vtss/service/ras/v1/login
Content-Type: application/json

{
  "client_id": "<tenant_mysign_client_id>",
  "client_secret": "<tenant_mysign_client_secret>",
  "profile_id": "<profile_id>",
  "user_id": "<user_email>"
}
```

Response: `{ "access_token": "...", "expires_in": 3600 }`

Cached in-memory (IMemoryCache) with TTL = expires_in - 60s leeway.

**2. Get Certificate Info**

```http
POST https://remotesigning.viettel.vn/vtss/service/certificates/info
Authorization: Bearer <access_token>
Content-Type: application/json

{}
```

Response: `{ "certificates": [{ "serial": "01:2A:...", "subject": "CN=User", ... }] }`

**3. Sign Hash (Sync)**

```http
POST https://remotesigning.viettel.vn/vtss/service/signHash
Authorization: Bearer <access_token>
Content-Type: application/json

{
  "documents": [
    {
      "document_id": "doc-1",
      "hash": "base64(hash_bytes)"
    }
  ],
  "hashAlgorithm": "SHA256",
  "signatureAlgorithm": "RSA",
  "async": 0
}
```

Response (success):
```json
{
  "meta_data": { "status": "2000", "message": "OK" },
  "data": {
    "results": [
      {
        "document_id": "doc-1",
        "signature": "base64(...)"
      }
    ]
  }
}
```

Response (pending user):
```json
{
  "meta_data": { "status": "4000", "message": "Pending" },
  "data": { "transactionId": "tx-2026-05-11-12-34-56-xyz" }
}
```

**4. BE Response to FE**

If sync succeeds (status=2000):
```json
{
  "success": true,
  "data": {
    "isSuccess": true,
    "signature": "base64(...)",
    "signingCertificate": { /* cert info */ }
  }
}
```

---

## 8. Viettel Mysign — Async-Poll Mode (async=2)

**Use case:** Long-running signature where user confirms on phone while FE polls.

### 8.1 InlinePoll vs DeferredPoll Trade-off

| Approach | Pros | Cons |
|----------|------|------|
| **InlinePoll** | FE continuously polls BE; BE continuously polls Viettel | Simple; real-time updates; Viettel API quota exhaustion |
| **DeferredPoll** | FE polls BE once; BE stores tx state; background job polls Viettel | Efficient; scale-friendly; slight latency added |

**AqtCoreFW default:** DeferredPoll (production-grade).

### 8.2 Request

```json
{
  "extraParameters": {
    "async": "2"
  }
}
```

**Viettel response:**
```json
{
  "meta_data": { "status": "4000", "transactionId": "tx-abc-123" },
  "data": { }
}
```

**BE stores:** `SigningPendingTransaction` entity:
- TransactionId: tx-abc-123
- Provider: remote-viettel-mysign
- TenantId, UserId, Reason
- Status: Pending (4000)
- CreatedAt: now
- ExpiresAt: now + 24h

### 8.3 Status Code Table

| Status | Meaning | Terminal | Success | Action |
|--------|---------|----------|---------|--------|
| 4000 | Waiting for user confirmation on mobile | | | Poll again in 3s |
| 6000 | User confirmed; CA signing in progress | | | Poll again in 3s |
| 2000 | Completed successfully | ✓ | ✓ | Fetch signature; store in DB |
| 4001 | Timeout (user didn't confirm) | ✓ | | Return error to FE |
| 4002 | User rejected on phone | ✓ | | Return error to FE |
| 4004 | CA signing error | ✓ | | Return error to FE |
| 4005 | Insufficient balance in user account | ✓ | | Return error to FE |
| 13003 | Certificate expired | ✓ | | Return error to FE |
| 13004 | Certificate invalid/revoked | ✓ | | Return error to FE |
| 50000 | Viettel internal error | ✓ | | Retry or escalate |

### 8.4 FE Polling (POST /api/v1/digital-signature/confirm)

```json
{
  "transactionId": "tx-abc-123"
}
```

**BE polls Viettel** every 3 seconds up to 120 attempts (6 minutes):

```http
POST https://remotesigning.viettel.vn/vtss/service/requests/status
Authorization: Bearer <access_token>

{ "requestId": "tx-abc-123" }
```

**Response back to FE:**

```json
{
  "success": true,
  "data": {
    "status": "2000",
    "signature": "base64(...)",
    "signingCertificate": { /* cert */ }
  }
}
```

**When timeout (6 min elapsed):** Return 408 Request Timeout + "Signature operation cancelled".

---

## 9. Viettel Mysign — Async-Callback Mode (async=3)

**Use case:** Mobile users may take minutes to confirm; FE subscribes to webhook callbacks instead of polling.

### 9.1 Request

```json
{
  "extraParameters": {
    "async": "3"
  }
}
```

Viettel response: `{ "status": "4000", "transactionId": "tx-xyz" }`

### 9.2 Webhook: Callback Auth

**Incoming request from Viettel:**

```http
POST https://api.example.com/api/signing/mysign-callback/auth

Content-Type: application/json
{
  "grant_type": "client_credentials",
  "client_id": "<mysign_client_id>",
  "client_secret": "<mysign_client_secret>"
}
```

**BE validation (MysignCallbackAuthController):**

1. Check config `Signing:Providers:ViettelMysign:CallbackInboundClientId/Secret`
2. Constant-time compare against incoming credentials
3. If match: Issue short-lived Bearer token (expires_in=3600)
4. Return 200 OK with `{ "status_code": "00", "data": { "access_token": "...", "expires_in": 3600 } }`
5. If no match: Return 401 Unauthorized

### 9.3 Webhook: Result Callback

**Incoming request from Viettel:**

```http
POST https://api.example.com/api/signing/mysign-callback/result

Authorization: Bearer <inbound_token>
Content-Type: application/json

{
  "meta_data": {
    "transaction_id": "tx-xyz",
    "status": "2000",
    "timestamp": "2026-05-11T12:34:56Z"
  },
  "data": {
    "results": [
      {
        "document_id": "doc-1",
        "signature": "base64(...)",
        "certificateInfo": {
          "serial": "01:2A:...",
          "subject": "CN=Approver"
        }
      }
    ]
  }
}
```

**BE handling (MysignCallbackResultController):**

1. Verify Authorization header (Bearer token from earlier auth call)
2. Look up `SigningPendingTransaction` by transaction_id
3. **Idempotency:** If already processed, return 200 OK silently (don't re-store)
4. If new: Update tx Status to 2000; store Signature + Cert in entity
5. Optionally trigger domain event (e.g., SignatureCompletedIntegrationEvent) for down-stream processors
6. Return 200 OK with `{ "status_code": "00" }`

---

## 10. ConfirmAsync (Modes 2 & 3)

**Endpoint:** `POST /api/v1/digital-signature/confirm`

**Request:**
```json
{
  "transactionId": "tx-abc-123"
}
```

**Mode-aware dispatch:**

```csharp
var tx = await signingPendingTransactionRepo.GetByTransactionIdAsync(txId, cancellationToken);

if (tx == null)
{
    return NotFound("Transaction not found or expired");
}

if (tx.Status == "2000")  // Completed
{
    return Ok(new SignResultDto { 
        Signature = tx.Signature, 
        SigningCertificate = tx.CertificateInfo 
    });
}

if (tx.Status == "4000" || tx.Status == "6000")  // Still pending
{
    // Async-callback mode: Mysign already called callback, tx populated
    // Async-poll mode: BE polls Viettel here
    
    var status = await mysignClient.GetStatusAsync(txId, cancellationToken);
    tx.Status = status;
    
    if (status == "2000")
    {
        return Ok(...);
    }
    else if (status == "4000" || status == "6000")
    {
        return Accepted();  // 202: still waiting
    }
    else
    {
        return BadRequest($"Signature failed: {status}");
    }
}

// Terminal error
return BadRequest(tx.FailureReason);
```

---

## 11. Certificate Chain Validation

**Implementation:** `CertificateChainValidator` (Identity.Infrastructure).

Uses BouncyCastle PKIX library + custom OCSP client.

### 11.1 Trust Anchors

```csharp
var vnCaRootStore = new VnCaRootStore();
// Populated from Vault (or hardcoded for dev)
// Keys: "VNPT-CA Root", "VGCA Root", etc.
// Values: X509Certificate2 objects (PEM-decoded)
```

**Production setup:**
- Root CA certs sourced from `https://rootca.gov.vn/` (NEAC — National e-ID Authority)
- Seeding mechanism: Background job runs weekly; updates Vault `aqt/encryption/ca-roots` mount
- Currently: VnCaRootStore `_productionThumbprints = []` (empty) — NEAC seeding deferred to post-Phase-05b

### 11.2 OCSP Validation

```csharp
var chainResult = await certificateChainValidator.ValidateAsync(
    leafCert,
    intermediates,
    ocspMode: OcspMode.Soft,  // or Hard
    cancellationToken
);
```

| Mode | Behavior | Use Case |
|------|----------|----------|
| **Soft** | OCSP failure is logged but does not block signature acceptance | Development, fallback scenarios |
| **Hard** | OCSP failure blocks signature (returns IsValid=false) | Production e-invoice / critical approvals |

**OCSP Responder:**
- VNPT CA: http://ocsp.vnpt-ca.vn/
- VGCA: http://ocsp.vgca.com.vn/
- Caching: 6-hour window (responder signature validity)

---

## 12. Tenant Configuration

**Entity:** `TenantSignatureConfig` (Identity.Domain.Entities)

```csharp
public sealed record TenantSignatureConfig(
    Guid TenantId,
    string ProviderKey,           // "usb-vgca" or "remote-viettel-mysign"
    IReadOnlyDictionary<string, string>? ProviderSettings,  // serialized JSON
    DateTime CreatedAt,
    DateTime? UpdatedAt = null,
    byte[]? RowVersion = null
);
```

### 12.1 Admin UI Workflow

**GET** `/api/v1/admin/signing/tenant-config/{tenantId}`

Returns current config + available providers list.

**POST** `/api/v1/admin/signing/tenant-config`

```json
{
  "tenantId": "550e8400-e29b-41d4-a716-446655440000",
  "providerKey": "remote-viettel-mysign",
  "providerSettings": {
    "clientId": "VIETTEL_CLIENT_ID",
    "clientSecret": "{{ vault:aqt/signing/viettel-mysign/client-secret }}",
    "profileId": "PROFILE_001"
  }
}
```

**Validation:**
- ProviderKey must be registered in DI (architecture guard catches undefined)
- If switching from VGCA→Viettel (or vice versa), existing pending transactions marked terminal (failure)
- Pre-validation: test sign + verify round-trip with test data

**Downtime considerations:**
- No service restart required (DI resolution happens per-request)
- In-flight signing operations complete with their original provider
- New sign requests immediately use updated config

---

## 13. Audit & Compliance (QĐ 742 Mục 5.1)

Every sign/verify/confirm operation emits audit events.

**IAuditLogger.LogAsync contract:**

```csharp
await auditLogger.LogAsync(new AuditLogRequest(
    Action: "DigitalSignatureProvided",
    ResourceType: "Signature",
    ResourceId: payloadHash,
    Details: new
    {
        Provider = "usb-vgca",
        CertificateSerialNumber = "01:2A:3B:...",
        CertificateSubject = "CN=User Name,O=Org",
        PayloadHashSha256 = "abc123...",
        SignatureFormat = "Raw",
        Outcome = "Success",
        Severity = "Critical"  // Per QĐ 742 phụ lục requirement
    }
));
```

**Architecture Guards:**

```csharp
// SignatureProviderCapabilityArchTests
[Fact]
public void AllProviders_DeclareCapabilities_Consistently()
{
    // Validates:
    // 1. Each provider declares ≥1 capability
    // 2. Capability flags match method support (RawSign → SignAsync works, etc.)
    // 3. Tier-1 raw requires RawSign; Tier-2 requires XmlDsig/JsonSign; Tier-3 requires DocFlow
}

// CallbackAuthArchTests
[Fact]
public void MysignCallbackAuth_UsesConstantTimeCompare()
{
    // Validates: CryptographicOperations.FixedTimeEquals used (not ==)
}
```

---

## 14. Provider Extensibility (D6 Acceptance Gate)

**Design:** Adding a new VN CA provider requires **zero changes** to central registries.

### 14.1 Steps to Add "FPT-CA" Provider

**1. Implement interface:**

```csharp
public sealed class FptCaProvider : IDigitalSignatureProvider
{
    public string ProviderKey => "remote-fpt-ca";
    public string DisplayName => "FPT-CA Remote Signing";
    public SignatureProviderCapabilities Capabilities =>
        SignatureProviderCapabilities.ServerSideSigning |
        SignatureProviderCapabilities.RawSign;

    public async Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
    {
        // FPT API integration
    }

    public async Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken ct = default)
    {
        // Chain validation via FPT root store
    }
}
```

**2. Register in DI (InfrastructureRegistration.cs):**

```csharp
services.AddKeyedScoped<IDigitalSignatureProvider, FptCaProvider>("remote-fpt-ca");
```

**3. Use in tenant config:**

```json
{
  "providerKey": "remote-fpt-ca",
  "providerSettings": { "clientId": "...", "clientSecret": "..." }
}
```

**4. Architecture tests auto-validate:**

The `SignatureProviderExtensibilityTests` scans all registered providers via reflection and validates:
- Capabilities non-empty
- Sign/Verify methods callable
- Correct exception on unsupported capability

**No updates needed to:**
- `ISignatureProviderResolver`
- `SignRequest` / `VerifyRequest` records
- Controllers
- FE provider registry

---

## 15. Production Gates & Known Limitations

### 15.1 NEAC Root Certificate Seeding

**Status:** ⏳ DEFERRED (post-Phase-05b)

**Impact:** Real-world certificate chain validation will **reject all signatures** until NEAC roots seeded.

**Workaround (dev):** Set `Signing:Ocsp:Mode=Soft` in appsettings; OCSP failures logged but don't block.

**Resolution steps:**
1. Contact NEAC (National e-ID Authority) for root CA export
2. Run seeding job to populate Vault `aqt/encryption/ca-roots` mount
3. Update `VnCaRootStore._productionThumbprints` with seeded roots
4. Deploy to production

**Lead time:** 3-5 business days (NEAC approval required).

### 15.2 Viettel Partner Contract

**Status:** Sandbox credentials only (dev/QA testing)

**Missing:** Production `client_id` + `client_secret` require signed partnership agreement with Viettel.

**Current state:** WireMock stubs all Viettel API responses for testing.

**E2E testing:** Once contract signed, update `appsettings.Production.json` with real credentials; integration tests pass against Viettel sandbox API.

### 15.3 Stale Transaction Cleanup

**Job:** Background task runs daily; deletes `SigningPendingTransaction` rows older than 24h.

**Config:** `Signing:PendingTransactionTtl` (default: 24 hours)

**Monitoring:** Alert if cleanup job fails or pending table grows unbounded.

### 15.4 Vault Key Wire-up (Phase-06 Completed)

**Signing-related Vault paths:**
- `aqt/encryption/docflow-jwt-sign` — JWT signing key for doc-flow (5-min token TTL)
- `aqt/encryption/v{N:000}` — Cipher key versioning (EncryptedStringConverter)
- `aqt/signing/viettel-mysign/client-secret` — Viettel API credentials

All keys rotated via `docs/ops/vault-key-rotation-runbook.md` (Phase-06 delivery).

---

## 16. References

- **D17/D18/D19 Spec Amendments:** `docs/signing/spec-amendments.md`
- **Dependency Map:** `docs/dependency-map.md` (Signing module entities + endpoints)
- **QĐ 742 Compliance Dossier:** `docs/compliance/qd-742-dossier.md` (mục 5.1 evidence)
- **Vault Rotation Runbook:** `docs/ops/vault-key-rotation-runbook.md`
- **VGCA SDK:** `other/vgca_usb_token/` (on-disk; MSI + JavaScript plugin)
- **Viettel Mysign API:** `other/remote_signing_viettel/` (extracted PDF; v1.9 reference)
- **Key Rotation Guide:** `docs/ops/vault-key-rotation-runbook.md`

---

**Production deployment checklist:**
- [ ] NEAC root certs seeded (minimum 3-5 business days lead time)
- [ ] Viettel contract signed + sandbox creds confirmed
- [ ] Vault keys initialized (Phase-06)
- [ ] OCSP responders reachable from production network
- [ ] Audit pipeline tested end-to-end
- [ ] FE digital-signature feature flag enabled by default
- [ ] Rate limits configured for /api/v1/digital-signature/* routes
- [ ] MFA required for signing operations (RequiresMfa attribute active)
- [ ] Runbooks: vault rotation, NEAC seeding, cert expiry alerts
