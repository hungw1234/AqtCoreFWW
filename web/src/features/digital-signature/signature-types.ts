/**
 * signature-types.ts — shared DTOs and enums for digital-signature feature.
 * Mirrors BE Identity.Domain shapes exactly — verified against BE source 2026-05-11.
 *
 * JSON serialization notes (ASP.NET Core 8 default):
 * - Enum fields: JsonStringEnumConverter registered in Program.cs → arrive as string names.
 * - [Flags] enums: arrive as comma-separated names, e.g. "ServerSideSigning, RequiresLocalAgent".
 * - byte[] fields: arrive as base64 string (System.Text.Json default).
 * - Property names: camelCase (ASP.NET Core default, no explicit override detected in Program.cs).
 */

// ─── OcspMode ─────────────────────────────────────────────────────────────────
// Mirrors BE: public enum OcspMode { Soft, Hard }
export type OcspMode = 'Soft' | 'Hard';

// ─── SignatureFlowMode ──────────────────────────────────────────────────────────
// Mirrors BE: public enum SignatureFlowMode { Async, Sync }
// Per-tenant control over the remote (Viettel Mysign PAdES) signing flow:
//   Async (default) — non-blocking, finalize via a background job; safe behind short request timeouts.
//   Sync — block on the mobile OTP in-request (~120s); opt-in (gateway-timeout + orphan-signature trade-offs).
export type SignatureFlowMode = 'Async' | 'Sync';

// ─── SignatureProviderCapabilities ────────────────────────────────────────────
// Mirrors BE: [Flags] public enum SignatureProviderCapabilities { None=0, ServerSideSigning=1, … }
// Wire format: comma-separated string (JsonStringEnumConverter + [Flags]).
// e.g. "ServerSideSigning, RequiresLocalAgent"
export type CapabilityName =
  | 'None'
  | 'ServerSideSigning'
  | 'ClientSideSigning'
  | 'BatchSigning'
  | 'LongTermValidation'
  | 'OcspStapling'
  | 'RequiresLocalAgent'
  | 'RawSign'    // Tier-1 raw hash-based sign (W1)
  | 'XmlDsig'   // Tier-2 XMLDSig (W2)
  | 'JsonSign'   // Tier-2 JWS compact (W2)
  | 'DocFlow';   // Tier-3 doc-flow PDF signing (W3)

/**
 * Parse comma-separated capability string from BE and check if a specific capability is present.
 * Usage: hasCapability(capabilities, 'RequiresLocalAgent')
 */
export function hasCapability(capabilities: string, cap: CapabilityName): boolean {
  const parts = capabilities.split(',').map((s) => s.trim());
  return parts.includes(cap);
}

// ─── CertificateInfo ──────────────────────────────────────────────────────────
// Mirrors BE: public record CertificateInfo(string SerialNumber, string Subject, …)
export interface CertificateInfo {
  serialNumber: string;
  subject: string;
  issuer: string;
  notBefore: string; // ISO date string (DateTime)
  notAfter: string;
  thumbprint: string;
  rawData: string; // base64-encoded byte[]
}

// ─── ChainValidationResult ────────────────────────────────────────────────────
// Mirrors BE: public record ChainValidationResult(bool IsValid, …)
export interface ChainValidationResult {
  isValid: boolean;
  failureReason: string | null;
  chain: CertificateInfo[];
  ocspChecked: boolean;
  ocspSucceeded: boolean;
}

// ─── Admin config DTOs ────────────────────────────────────────────────────────

/**
 * GET /api/v1/admin/security/signature-config/{tenantId}
 * Mirrors BE: TenantSignatureConfigDto
 */
export interface TenantSignatureConfigDto {
  tenantId: string;
  providerKey: string;
  apiCredentialsRef: string | null;
  allowedCaRootThumbprints: string;
  ocspMode: OcspMode;
  isEnabled: boolean;
  signFlowMode: SignatureFlowMode; // remote-signing flow: Async (default) | Sync
  rowVersion: string; // base64-encoded byte[] (optimistic concurrency)
  createdAt: string;
  updatedAt: string | null;
}

/**
 * PUT /api/v1/admin/security/signature-config/{tenantId}
 * Mirrors BE: UpsertSignatureConfigRequest (record at SignatureConfigAdminController.cs:96)
 * RowVersion: base64-encoded EF concurrency token from prior GET — null for first upsert.
 */
export interface UpdateSignatureConfigRequest {
  providerKey: string;
  apiCredentialsRef: string | null;
  allowedCaRootThumbprints: string;
  ocspMode: OcspMode;
  isEnabled: boolean;
  signFlowMode: SignatureFlowMode; // remote-signing flow: Async (default) | Sync
  rowVersion?: string | null;
}

/**
 * GET /api/v1/admin/security/signature-config/available-providers
 * Mirrors BE: AvailableProviderDto
 */
export interface AvailableProviderDto {
  providerKey: string;
  displayName: string;
  capabilities: string; // comma-separated CapabilityName (from [Flags] enum)
}

/**
 * GET /api/v1/digital-signature/provider-info
 * Mirrors BE SigningProviderInfoDto. Non-admin, JWT-tenant-scoped read used by the form-fill
 * signing modal to pick the right client adapter (USB-local vs remote OTP) and detect
 * RequiresLocalAgent. capabilities = comma-separated CapabilityName (from [Flags] enum).
 */
export interface SigningProviderInfo {
  isEnabled: boolean;
  providerKey: string | null;
  capabilities: string;
}

// ─── Signing/Verify DTOs ──────────────────────────────────────────────────────

/**
 * POST /api/v1/digital-signature/sign (request body)
 * Mirrors BE: SignRequest (Presentation layer)
 * payload: base64-encoded bytes; extraParameters: optional key-value bag.
 */
export interface SignRequest {
  payload: string; // base64-encoded
  reason?: string;
  extraParameters?: Record<string, string>;
}

/**
 * POST /api/v1/digital-signature/sign (response)
 * Mirrors BE: SignPayloadResultDto
 * signature: base64-encoded byte[]? (null when isPending=true)
 */
export interface SignResponse {
  isSuccess: boolean;
  isPending: boolean;
  signature: string | null; // base64-encoded; null when isPending
  transactionId: string | null;
  failureReason: string | null;
  signingCertificate: CertificateInfo | null;
}

/**
 * Signature binary layout. Mirrors BE SignatureFormat enum (wire = string-named via
 * JsonStringEnumConverter). 'Raw' = bare RSA/ECDSA over a precomputed hash with the
 * signer cert supplied separately (VGCA USB token + Viettel Mysign); 'Pkcs7' = CMS
 * envelope that embeds its own cert.
 */
export type SignatureFormat = 'Raw' | 'Pkcs7';

/**
 * POST /api/v1/digital-signature/verify (request body)
 * Mirrors BE: VerifyRequest (Presentation layer)
 * payload + signature: base64-encoded bytes.
 *
 * Raw-format providers (usb-vgca, remote-viettel-mysign) MUST send format='Raw' +
 * signerCertBase64; omitting them makes BE default to Pkcs7 and route the bare RSA
 * signature into the CMS parser ("CMS signature could not be parsed").
 */
export interface VerifyRequest {
  payload: string; // base64-encoded
  signature: string; // base64-encoded
  ocspModeOverride?: OcspMode; // null → BE uses tenant config default
  signerCertBase64?: string; // base64 DER signer cert — required when format='Raw'
  format?: SignatureFormat; // omit → BE default 'Pkcs7'
  hashAlg?: string; // e.g. "SHA256" (BE default)
  encode?: string; // "RSA" (BE default) or "ECDSA"
}

/**
 * POST /api/v1/digital-signature/verify (response)
 * Mirrors BE: VerifySignatureResultDto
 */
export interface VerifyResponse {
  isValid: boolean;
  failureReason: string | null;
  signingCertificate: CertificateInfo | null;
  chainResult: ChainValidationResult | null;
}

// ─── Adapter contract ─────────────────────────────────────────────────────────

export interface SignOpts {
  reason?: string;
  /** W1 Tier-1: asymmetric algorithm to request from VGCA — "RSA" (default) or "ECDSA". */
  encode?: 'RSA' | 'ECDSA';
}

/**
 * Options for vgca_sign_xml — W2 Tier-2 XMLDSig.
 * Maps to the JSON params object passed to vgca_sign_xml(sender, params, cb).
 *
 * SDK_VERIFICATION_PENDING: DefaultECC.htm empirical sample uses XmlDsigForm:"false" and HashAlg:"Sha256".
 * Task spec uses signatureOnly=false and hashAlg="SHA256". Using task spec values pending hardware test.
 */
export interface SignXmlOpts {
  /** When true, return only the <Signature> fragment; false (default) = return entire signed document. */
  signatureOnly?: boolean;
  /** Hash algorithm passed to VGCA — default "SHA256". */
  hashAlg?: string;
  /**
   * XMLDSig form. SDK sample uses string "false" (= full enveloped doc); task spec uses "Enveloped".
   * SDK_VERIFICATION_PENDING: empirical value "false" may override this on hardware.
   */
  form?: string;
}

/**
 * Options for vgca_sign_json — W2 Tier-2 JWS compact.
 * Maps to the JSON params object passed to vgca_sign_json(sender, params, cb).
 *
 * SDK_VERIFICATION_PENDING: DefaultECC.htm uses IsPss:"true" string; task spec uses usePss boolean.
 * Sending boolean value pending hardware verification.
 */
export interface SignJsonOpts {
  /** When true, detach (omit) the payload from the JWS; false (default) = embedded payload. */
  detach?: boolean;
  /** When true, use PSS padding for RSA; false (default) = PKCS1v1.5. */
  usePss?: boolean;
  /** Hash algorithm — default "SHA256". */
  hashAlg?: string;
}

export interface SignResult {
  signatureBase64: string;
  providerKey: string;
  isPending: boolean;
  transactionId?: string;
  /** W1 Tier-1: base64-encoded DER certificate returned by VGCA alongside the raw signature. */
  signerCertBase64?: string;
  /** W1 Tier-1: hash algorithm used (e.g. "SHA256"). */
  hashAlg?: string;
  /** W1 Tier-1: asymmetric algorithm used (e.g. "RSA", "ECDSA"). */
  encode?: string;
}

// ─── Tier-3 doc-flow types (W3) ──────────────────────────────────────────────

/**
 * Sign-type for VGCA Tier-3 doc-flow — mirrors BE DocFlowSignType enum.
 * Maps 1-to-1 to the 7 VGCA plugin function names.
 */
export type DocFlowSignType =
  | 'Issued'    // vgca_sign_issued
  | 'Approved'  // vgca_sign_approved
  | 'Income'    // vgca_sign_income
  | 'Comment'   // vgca_comment
  | 'Appendix'  // vgca_sign_appendix
  | 'Copy'      // vgca_sign_copy
  | 'Files';    // vgca_sign_files (batch)

/**
 * Metadata for VGCA doc-flow signing — maps to DocFlowMetadata on BE.
 * Passed to /initiate and forwarded to the VGCA plugin function params.
 */
export interface DocFlowMetadata {
  /** Force filename in BE storage (null → auto-generated from docNumber). */
  fileName?: string | null;
  /** Document reference number (số hiệu văn bản) — shown in visible stamp. */
  docNumber: string;
  /** Issuance date (ngày ban hành) — shown in visible stamp. */
  issuedDate: Date;
  /**
   * Storage key of the original (unsigned) PDF in "signing-original" bucket.
   * FE must supply this because the BE has no doc registry.
   */
  originalStorageKey: string;
  /** Optional extra key-value pairs for custom stamp fields. */
  extra?: Record<string, string> | null;
}

/**
 * POST /api/signing/docflow/initiate request body.
 * Matches BE InitiateDocFlowRequest record.
 */
export interface DocFlowInitiateRequest {
  docId: string;
  signType: DocFlowSignType;
  originalStorageKey: string;
  docNumber: string;
  issuedDate: string; // ISO 8601 date string
  fileName?: string | null;
  extra?: Record<string, string> | null;
}

/**
 * POST /api/signing/docflow/initiate response.
 * Matches BE InitiateDocFlowResultDto.
 */
export interface DocFlowInitiateResponse {
  handlerUrl: string;
  jwt: string;
  docId: string;
}

/**
 * Result returned by adapter doc-flow methods (signIssued, signApproved, etc.).
 * Populated from the VGCA plugin callback response.
 */
export interface DocFlowAdapterResult {
  status: 'Completed';
  /** FileServer URL returned by VGCA (points to the signed PDF in BE storage). */
  storageKey: string | null;
  /** Raw FileServer value from VGCA callback. */
  fileServer?: string;
}

/**
 * Certificate preview returned by the adapter's getCertInfo() method.
 * Populated from the VGCA vgca_get_certinfo(cb) callback fields.
 */
export interface CertPreview {
  subject: string;
  issuer: string;
  validFrom: Date;
  validTo: Date;
  serialNumber: string;
  /** Base64-encoded DER certificate bytes. */
  certBase64: string;
}

// ─── Provider-agnostic signing request endpoints ──────────────────────────────

/**
 * Artifact kind for the unified signing request.
 * Mirrors BE: SigningArtifactKind enum (wire = string name via JsonStringEnumConverter).
 */
export type SigningArtifactKind = 'DetachedData' | 'FormPdf' | 'UploadedPdf';

/**
 * VGCA doc-flow sign-type for the unified request body.
 * Identical value set to DocFlowSignType — kept as a separate alias used in the
 * new unified body so callers import from one place without cross-contaminating the
 * legacy initiateDocFlow path.
 */
export type DocFlowSignTypeName =
  | 'Issued'
  | 'Approved'
  | 'Income'
  | 'Comment'
  | 'Appendix'
  | 'Copy'
  | 'Files';

/**
 * Lifecycle status of a signing request, computed at read-time on the BE.
 * Mirrors BE: SigningRequestStatus enum (wire = string name via JsonStringEnumConverter).
 * Terminal statuses (polling must stop): Completed, Failed, Expired.
 */
export type SigningRequestStatus = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Expired';

/** Terminal statuses — polling stops when any of these is received. */
export const SIGNING_REQUEST_TERMINAL_STATUSES: readonly SigningRequestStatus[] = [
  'Completed',
  'Failed',
  'Expired',
];

/**
 * DocFlow-specific parameters inside CreateSigningRequestBody.
 * Required when the artifact routes to the USB doc-flow strategy; null otherwise.
 * Mirrors BE: CreateSigningRequestDocFlowRequest record.
 */
export interface CreateSigningRequestDocFlowBody {
  /** Which VGCA plugin function to invoke. */
  signType: DocFlowSignTypeName;
  /** Tenant-prefixed key of the unsigned PDF in the signing-original bucket. */
  originalStorageKey: string;
  /** Document reference number shown in the visible stamp. */
  docNumber: string;
  /** ISO 8601 date string (YYYY-MM-DD). */
  issuedDate: string;
  /** Optional forced storage filename (null → auto). */
  fileName?: string | null;
  /** Optional extra stamp metadata. */
  extra?: Record<string, string> | null;
}

/**
 * PAdES-specific parameters inside CreateSigningRequestBody.
 * Required when the artifact routes to the remote (Viettel Mysign) server-side strategy; null otherwise.
 * Mirrors BE: CreateSigningRequestPadesRequest record.
 */
export interface CreateSigningRequestPadesBody {
  /** Tenant-prefixed key of the unsigned PDF in the signing-original bucket (from stage-for-signing). */
  originalStorageKey: string;
  /** Optional label shown on the signer's Mysign mobile app during OTP; null → a default. */
  documentName?: string | null;
  /**
   * Optional per-request override of the tenant's remote signing flow. Null/omitted → use the tenant config
   * (default Async). Wins over TenantSignatureConfig.signFlowMode for this one request only.
   */
  signFlowMode?: SignatureFlowMode | null;
  /**
   * Optional user-entered reason rendered on the visible stamp's "Lý do" line (distinct from documentName).
   * Null/blank → the stamp omits the reason line.
   */
  reason?: string | null;
}

/**
 * POST /api/signing/request — request body.
 * Mirrors BE: CreateSigningRequestRequest record.
 * baseUrl is NOT accepted from the client (derived server-side).
 */
export interface CreateSigningRequestBody {
  artifactKind: SigningArtifactKind;
  /** Artifact reference: docId (GUID) for DocFlow, form submission id for data, storage key for PDF. */
  artifactRef: string;
  /** Configured provider key (e.g. "usb-vgca", "remote-viettel-mysign"). */
  providerKey: string;
  /** Optional placement / appearance hint (e.g. PDF coordinate JSON). */
  placement?: string | null;
  /** Required when routing to the USB doc-flow strategy; null otherwise. */
  docFlow?: CreateSigningRequestDocFlowBody | null;
  /** Required when routing to the remote PAdES strategy; null otherwise. */
  pades?: CreateSigningRequestPadesBody | null;
  /**
   * Files module FileRecord PublicId — required when artifactKind === 'UploadedPdf'.
   * Links the signing request back to the uploaded file record so the Files page can surface
   * signing steps (GET /files/{id}/signing-steps). Without this link the step list is always empty.
   */
  sourceFileId?: string | null;
}

/**
 * POST /api/signing/request — success result.
 * Mirrors BE: CreateSigningRequestResultDto.
 * Wire: { requestId: string (GUID), clientHandoff: Record<string,string> }
 * For DocFlow, clientHandoff has keys "handlerUrl", "jwt", "docId".
 */
export interface CreateSigningRequestResult {
  /** Public id of the created signing request — use to poll /status. */
  requestId: string;
  /** Strategy-specific hand-off data for the client (e.g. VGCA plugin params). */
  clientHandoff: Record<string, string>;
}

/**
 * GET /api/signing/request/{publicId}/status — response.
 * Mirrors BE: SigningRequestStatusDto.
 * Wire: all camelCase, status as string (JsonStringEnumConverter).
 */
export interface SigningRequestStatusDto {
  publicId: string;
  status: SigningRequestStatus;
  artifactKind: SigningArtifactKind;
  providerKey: string;
  failureReason: string | null;
}
