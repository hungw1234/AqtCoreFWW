/**
 * signed-document-api.ts — API calls for reading signing steps and downloading signed PDFs.
 *
 * Both Files and Forms expose identical response shapes; `scope` selects the URL prefix:
 *   files  → /api/v1/files/{id}/signing-steps  and  /api/v1/files/{id}/signed?sequence=N
 *   forms  → /api/v1/forms/submissions/{id}/signing-steps  and  /api/v1/forms/submissions/{id}/signed?sequence=N
 *
 * Blob download reuses the bearer-header pattern from file-api.ts:fetchFileBlob — a raw
 * <a href> or <embed src> cannot carry the Authorization header, so the token is sent via
 * apiClient and the response is converted to an objectURL by the caller.
 */

import { apiClient } from '@/core/api';
import type { SigningStepRow, SignedDocumentScope } from './signing-step-types';

// ─── URL builders ─────────────────────────────────────────────────────────────

function stepsUrl(scope: SignedDocumentScope, id: string): string {
  return scope === 'files'
    ? `/files/${id}/signing-steps`
    : `/forms/submissions/${id}/signing-steps`;
}

/**
 * Builds the signed-blob URL for one attempt. Files address by the per-attempt SigningRequest id
 * (so each independent sign of the same file — all at sequence 1 — is retrievable); Forms keep the
 * sequence addressing (a submission links to exactly one signing request, so it cannot collide).
 */
function signedBlobUrl(scope: SignedDocumentScope, id: string, step: SigningStepRow): string {
  if (scope === 'files') {
    return `/files/${id}/signed?requestId=${encodeURIComponent(step.signingRequestId)}`;
  }
  return `/forms/submissions/${id}/signed?sequence=${step.sequenceNumber}`;
}

// ─── Steps list ───────────────────────────────────────────────────────────────

/**
 * GET signing-steps for a given scope+id.
 * Returns rows ordered by sequenceNumber ascending (BE invariant).
 * Returns empty array — NOT an error — when no signing exists for this artifact.
 * The apiClient interceptor unwraps `{ data: T }` envelopes, so `r.data` is already `T`.
 */
export async function fetchSigningSteps(
  scope: SignedDocumentScope,
  id: string,
): Promise<SigningStepRow[]> {
  const r = await apiClient.get<SigningStepRow[]>(stepsUrl(scope, id));
  return r.data;
}

// ─── Signed PDF blob ──────────────────────────────────────────────────────────

/**
 * GET the signed PDF bytes for a specific step.
 * Returns a Blob (application/pdf) that the caller converts to an objectURL.
 * Throws (via apiClient) on 404 (step not Completed or cross-tenant) or 401.
 *
 * responseType:'blob' bypasses the envelope-unwrap interceptor — a Blob has no `success`
 * key so the interceptor passes it through untouched (same as fetchFileBlob in file-api.ts).
 */
export async function fetchSignedStepBlob(
  scope: SignedDocumentScope,
  id: string,
  step: SigningStepRow,
): Promise<Blob> {
  const r = await apiClient.get(signedBlobUrl(scope, id, step), {
    responseType: 'blob',
  });
  return r.data as Blob;
}
