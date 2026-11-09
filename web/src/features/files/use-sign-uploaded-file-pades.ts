/**
 * use-sign-uploaded-file-pades.ts — orchestration hook for signing an uploaded PDF via the REMOTE PAdES path
 * (Viettel Mysign, server-side hash signer).
 *
 * Unlike the USB DocFlow hook (use-sign-uploaded-file.ts), this path has NO client-side ceremony: the BE assembles
 * the PAdES PDF itself. The flow is simply:
 *   1. Stage the file (copy upload bucket → signing-original bucket, mint docId)
 *   2. Create a durable signing request with the `pades` parameters — the orchestrator initiates the Mysign
 *      signHash (async-poll) and returns a pollable requestId. The signer confirms the OTP on their phone.
 *   3. Return requestId so the caller polls useSigningRequestStatus until Completed; a server-side job finalizes
 *      the PDF, after which downloadSignedPdf(requestId) yields the signed bytes.
 */

import { useState, useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';
import { stageFileForSigning } from './file-signing-api';
import { createSigningRequest } from '@/features/digital-signature/signature-api';
import type { SignatureFlowMode } from '@/features/digital-signature/signature-types';

/** Provider key for the remote Viettel Mysign server-side PAdES strategy. */
export const REMOTE_MYSIGN_PROVIDER_KEY = 'remote-viettel-mysign';

export interface SignUploadedFilePadesParams {
  fileId: string;
  /** Label shown on the signer's Mysign mobile app during the OTP confirmation. */
  documentName?: string;
  /** User-entered reason rendered on the visible stamp's "Lý do" line. Blank/omitted → the line is omitted. */
  reason?: string;
  /**
   * Optional per-request override of the tenant's remote signing flow (Sync / Async). Null/omitted → the BE uses
   * the tenant config (default Async). Forwarded into the createSigningRequest `pades` sub-body.
   */
  signFlowMode?: SignatureFlowMode | null;
  /**
   * Optional visible-stamp placement as a JSON string (top-level CreateSigningRequestBody field — see
   * Identity SignaturePlacement: {page,x,y,w,h (bottom-left PDF points), profilePublicId, visualType}).
   * Null/omitted → the BE signs invisibly (backward-compatible). A non-positive rect is also treated as invisible.
   */
  placement?: string | null;
}

export interface UseSignUploadedFilePadesResult {
  isLoading: boolean;
  error: string | null;
  /** Public id of the created signing request — poll with useSigningRequestStatus once defined. */
  requestId: string | undefined;
  startSigning: (params: SignUploadedFilePadesParams) => Promise<void>;
  clearError: () => void;
  /** Clears requestId + error so a fresh run does not resume the previous request's terminal status. */
  reset: () => void;
}

export function useSignUploadedFilePades(): UseSignUploadedFilePadesResult {
  const queryClient = useQueryClient();
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [requestId, setRequestId] = useState<string | undefined>(undefined);

  const startSigning = useCallback(async (params: SignUploadedFilePadesParams) => {
    setIsLoading(true);
    setError(null);
    setRequestId(undefined);

    try {
      // Step 1: copy file to the signing-original bucket, mint docId.
      const staged = await stageFileForSigning(params.fileId);

      // Step 2: create the durable signing request on the remote PAdES strategy. The orchestrator submits the
      // Mysign signHash (async-poll) and returns immediately with a pollable requestId — no OTP block here.
      const created = await createSigningRequest({
        artifactKind: 'UploadedPdf',
        artifactRef: staged.docId,
        providerKey: REMOTE_MYSIGN_PROVIDER_KEY,
        sourceFileId: params.fileId,
        // Visible-stamp placement is a top-level hint; null → invisible sign (the BE's default).
        placement: params.placement ?? null,
        pades: {
          originalStorageKey: staged.originalStorageKey,
          documentName: params.documentName ?? null,
          signFlowMode: params.signFlowMode ?? null,
          reason: params.reason?.trim() ? params.reason.trim() : null,
        },
      });

      setRequestId(created.requestId);

      // Reflect signed state once the poll completes — same prefix-invalidation pattern as the DocFlow hook.
      queryClient.invalidateQueries({ queryKey: ['files', 'list'] });
    } catch (err) {
      setError(
        extractApiErrorMessage(err) ??
        (err instanceof Error ? err.message : null) ??
        'Ký số PAdES thất bại',
      );
    } finally {
      setIsLoading(false);
    }
  }, [queryClient]);

  const clearError = useCallback(() => setError(null), []);
  const reset = useCallback(() => {
    setError(null);
    setRequestId(undefined);
  }, []);

  return { isLoading, error, requestId, startSigning, clearError, reset };
}
