/**
 * file-signing-api.ts — API client for the stage-for-signing endpoint.
 *
 * POST /api/files/{id}/stage-for-signing copies the file bytes from the
 * upload bucket (aqtcorefw-files) to the signing bucket (signing-original)
 * under the same tenant-prefixed key, mints a docId, and returns the keys
 * needed by createSigningRequest. This staging step is required because the
 * VGCA DocFlow download reads from the signing-original bucket, not the
 * upload bucket — a direct key reference would 404.
 */

import { apiClient } from '@/core/api';
import { useQuery } from '@tanstack/react-query';

export interface StageForSigningResult {
  /** UUID minted for this signing session — used as artifactRef in createSigningRequest. */
  docId: string;
  /** Tenant-prefixed key in the signing-original bucket to pass as docFlow.originalStorageKey. */
  originalStorageKey: string;
}

/**
 * Stage an uploaded PDF for signing by copying it to the signing bucket.
 * Returns the docId and originalStorageKey needed by createSigningRequest.
 * Throws on HTTP error — callers must handle rejection.
 */
export async function stageFileForSigning(fileId: string): Promise<StageForSigningResult> {
  const r = await apiClient.post<StageForSigningResult>(`/files/${fileId}/stage-for-signing`);
  return r.data;
}

// Deployment-wide gate for signing an uploaded (untrusted) PDF. Off by default: the untrusted-PDF
// crypto-verify sandbox chain is deferred to a later release, and the BE rejects the request anyway
// (409) — this hides the entry so users don't hit a dead action. GET /api/v1/feature-flags/{key} is
// anonymous and returns { key, enabled }; the interceptor unwraps the envelope.
const UPLOAD_SIGN_FLAG = 'feature:signing.upload-arbitrary-pdf';

/**
 * True when this deployment permits signing user-uploaded PDFs. Defaults to false while loading, so the
 * upload-sign entry stays hidden until the flag is confirmed on — fail-closed for a gated path.
 */
export function useUploadSignEnabled(): boolean {
  const { data } = useQuery({
    queryKey: ['feature-flag', UPLOAD_SIGN_FLAG],
    queryFn: async () => {
      const r = await apiClient.get<{ key: string; enabled: boolean }>(
        `/feature-flags/${encodeURIComponent(UPLOAD_SIGN_FLAG)}`,
      );
      return r.data;
    },
    staleTime: 5 * 60 * 1000, // flag rarely flips; a 5-min cache avoids a request per page mount
  });
  return data?.enabled ?? false;
}
