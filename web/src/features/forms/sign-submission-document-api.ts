/**
 * sign-submission-document-api.ts — API client for the form-submission document signing endpoint.
 *
 * POST /api/forms/submissions/{publicId}/sign-document
 * BE composes the submission PDF, stages it to the signing-original bucket, issues a
 * signing request through the orchestrator, and returns the VGCA client handoff.
 * FE does NOT stage separately — one POST drives the entire compose+stage+initiate flow.
 *
 * Contract:
 *   Request body:  { signType, docNumber, issuedDate (ISO), providerKey }
 *   Response data: { requestId: string (GUID), clientHandoff: { handlerUrl, jwt, docId } }
 * Path passed to apiClient is `/forms/...`; apiClient prepends baseURL `/api/v1`, so the wire
 * URL is `/api/v1/forms/submissions/{publicId}/sign-document` — matches the controller route.
 */

import { apiClient } from '@/core/api';
import type { DocFlowSignTypeName } from '@/features/digital-signature/signature-types';

export interface SignSubmissionDocumentRequest {
  /** VGCA sign type — one of the 7 DocFlow sign functions. */
  signType: DocFlowSignTypeName;
  /** Document reference number shown in the VGCA visible stamp. */
  docNumber: string;
  /** ISO 8601 date string (YYYY-MM-DD) shown in the stamp. */
  issuedDate: string;
  /** Tenant-configured signing provider key (e.g. 'usb-vgca'). Required by the BE validator. */
  providerKey: string;
}

export interface SignSubmissionDocumentResult {
  /** Public id of the created signing request — use with useSigningRequestStatus to poll. */
  requestId: string;
  /** Strategy-specific hand-off for the client (DocFlow: handlerUrl / jwt / docId). */
  clientHandoff: Record<string, string>;
}

/**
 * POST /api/forms/submissions/{publicId}/sign-document
 * Kicks off the compose → stage → orchestrate flow server-side.
 * Returns the VGCA DocFlow handoff so the caller can invoke runVgcaPlugin directly
 * without a second /initiate (which would create a duplicate signing record).
 */
export async function signSubmissionDocument(
  publicId: string,
  body: SignSubmissionDocumentRequest,
): Promise<SignSubmissionDocumentResult> {
  const r = await apiClient.post<SignSubmissionDocumentResult>(
    `/forms/submissions/${publicId}/sign-document`,
    body,
  );
  return r.data;
}
