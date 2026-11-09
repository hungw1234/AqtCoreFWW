/**
 * signature-types.ts — FE DTO mirror for the self-service signature management tab (Phase 6 Slice A).
 *
 * Mirrors BE Identity shapes 1:1 — verified against source 2026-06-30:
 *   GET /api/v1/me/signature-overview          → SignatureOverviewDto
 *   GET/POST/PUT/DELETE /api/v1/me/signature-profiles[/{publicId}[/default]]
 *
 * JSON serialization (matches existing digital-signature/signature-types.ts):
 * - Enums: JsonStringEnumConverter (Program.cs) → arrive as string NAMES, not ints.
 * - Property names: camelCase (ASP.NET Core default).
 * - DateTime → ISO 8601 string; nullable DateTime → string | null.
 */

// ─── Enums (string-serialized) ─────────────────────────────────────────────────

// Mirrors BE: enum EnrollmentKind { RemoteViettelMysign = 1, UsbTokenVgca = 2 }
export type EnrollmentKind = 'RemoteViettelMysign' | 'UsbTokenVgca';

// Mirrors BE: enum CertApprovalStatus { Pending = 1, Approved = 2, Rejected = 3 }
export type CertApprovalStatus = 'Pending' | 'Approved' | 'Rejected';

// Mirrors BE: enum CertSubjectType { Personal = 0, Organization = 1 } (JsonStringEnumConverter — arrives as string name).
// Personal = natural-person cert (has CCCD/CMND RDN); Organization = org cert (no national-id RDN).
export type CertSubjectType = 'Personal' | 'Organization';

// Mirrors BE: enum SignatureVisualType { FullSignature = 1, InitialSignature = 2 }. Sent as the enum NAME on the
// multipart upload form field (ASP.NET model binder accepts the name); the ints are NOT the wire value here.
export type SignatureVisualType = 'FullSignature' | 'InitialSignature';

// ─── Profile ───────────────────────────────────────────────────────────────────

/**
 * Mirrors BE SignatureProfileDto. A profile is the display-name + default-flag container;
 * a linked cert (HasCertLinked) comes from a separate enrollment (Slice B). The hasFullImage /
 * hasInitialImage flags report which visual signature slots are populated (image upload slice).
 */
export interface SignatureProfileDto {
  publicId: string;
  displayName: string;
  isDefault: boolean;
  hasCertLinked: boolean;
  /** Added Phase 6 image slice: true when a full-signature image is stored for this profile. */
  hasFullImage: boolean;
  /** Added Phase 6 image slice: true when an initial/paraph image is stored for this profile. */
  hasInitialImage: boolean;
  /** Whether the visible PAdES stamp shows the "Serial" line for this profile (default true). */
  showSerial: boolean;
  /** Whether the visible PAdES stamp shows the "Nhà CC" (issuer) line for this profile (default true). */
  showIssuer: boolean;
  createdAt: string;
  updatedAt: string | null;
}

// ─── Enrollment status ───────────────────────────────────────────────────────────

/**
 * Mirrors BE SignatureEnrollmentStatusDto. Read-only in Slice A — the enroll flow that
 * creates these (Remote / USB) lands in Slice B. ApprovalStatus drives the status badge.
 */
export interface SignatureEnrollmentStatusDto {
  publicId: string;
  kind: EnrollmentKind;
  approvalStatus: CertApprovalStatus;
  isActive: boolean;
  serialNumber: string;
  issuer: string;
  subject: string | null;
  enrolledAt: string;
  rejectionReason: string | null;
  /** Added Phase 4: Personal = natural-person cert; Organization = org cert. Default Personal for historical rows. */
  subjectType: CertSubjectType;
  /** Added Phase 4: display name extracted from the cert subject DN (CN for personal, O/CN for org). Null for pre-Phase-4 rows. */
  displayName: string | null;
}

// ─── Overview (the tab's single read) ────────────────────────────────────────────

/**
 * Mirrors BE SignatureOverviewDto (GET /me/signature-overview). The signature tab MUST source
 * its data from here — NOT from OIDC claims, which lack SigningNationalId / enrollment status.
 */
export interface SignatureOverviewDto {
  nationalIdEstablished: boolean;
  hasApprovedEnrollment: boolean;
  profiles: SignatureProfileDto[];
  enrollments: SignatureEnrollmentStatusDto[];
}

// ─── Profile CRUD request bodies ─────────────────────────────────────────────────

// Mirrors BE CreateSignatureProfileRequest { DisplayName, ShowSerial, ShowIssuer }
export interface CreateSignatureProfileRequest {
  displayName: string;
  showSerial: boolean;
  showIssuer: boolean;
}

// Mirrors BE UpdateSignatureProfileRequest { DisplayName, ShowSerial, ShowIssuer }
export interface UpdateSignatureProfileRequest {
  displayName: string;
  showSerial: boolean;
  showIssuer: boolean;
}

// ─── Enrollment (Slice B) ────────────────────────────────────────────────────────

// Mirrors BE SetSigningNationalIdRequest — PUT /me/signing-national-id. CCCD = 12 digits.
export interface SetSigningNationalIdRequest {
  nationalId: string;
}

// Mirrors BE EnrollmentChallengeDto (POST /me/signature-cert/challenge). The challenge is the raw nonce
// (base64) the client must sign for the USB possession proof; nonceId ties the proof back to this challenge.
export interface EnrollmentChallengeDto {
  nonceId: string;
  challengeBase64: string;
  expiresAt: string;
}

// Mirrors BE EnrollSignatureCertRequest (POST /me/signature-cert/enroll). Kind-agnostic single route:
// Remote sends only {kind, nonceId} (server proves possession via Mysign OTP); USB also sends the locally
// signed nonce + DER cert read from the token.
export interface EnrollSignatureCertRequest {
  kind: EnrollmentKind;
  nonceId: string;
  signedNonceBase64?: string | null;
  certBase64?: string | null;
}
