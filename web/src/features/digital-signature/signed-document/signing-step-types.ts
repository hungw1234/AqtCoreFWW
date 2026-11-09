/**
 * signing-step-types.ts — Wire shape for a single signing-step row.
 * Matches the BE `SigningStepDto` serialized by both Files and Forms endpoints.
 * camelCase per ASP.NET Core default STJ policy.
 */

/** Lifecycle status of a single signing step. */
export type SigningStepStatus = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Expired';

/**
 * One row from GET /api/v1/files/{id}/signing-steps or
 * GET /api/v1/forms/submissions/{publicId}/signing-steps.
 * Both endpoints return the identical shape inside `{ data: SigningStepRow[] }`.
 */
export interface SigningStepRow {
  /**
   * Stable per-attempt id = the SigningRequest PublicId. Used as the React row key and as the blob
   * download address for the Files scope, so that a file signed multiple times (every sign shares
   * sequenceNumber 1) shows one row per sign and each row downloads its own signed PDF.
   */
  signingRequestId: string;
  sequenceNumber: number;
  status: SigningStepStatus;
  /** The signer's user-id claim. Always populated (non-null). */
  signerUserId: string;
  /**
   * Display name of the signer — null in current implementation.
   * UI falls back to "Người ký #N" (where N = sequenceNumber) to avoid a
   * user-lookup round-trip (KISS; full name lookup is a future capability).
   */
  signerName: string | null;
  /** ISO 8601 string when status=Completed; null otherwise. */
  completedAt: string | null;
  /** True when a signed PDF blob is available for download / viewing. */
  hasSignedPdf: boolean;
}

/**
 * Scope selector — drives URL construction in signed-document-api.ts.
 * 'files'  → /api/v1/files/{id}/...
 * 'forms'  → /api/v1/forms/submissions/{id}/...
 */
export type SignedDocumentScope = 'files' | 'forms';
