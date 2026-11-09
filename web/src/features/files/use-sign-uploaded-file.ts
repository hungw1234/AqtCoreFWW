/**
 * use-sign-uploaded-file.ts — orchestration hook for signing an uploaded PDF via USB DocFlow.
 *
 * Flow:
 *   1. Stage the file (copy upload bucket → signing bucket, mint docId)
 *   2. Create a durable signing request (unified endpoint) to get the VGCA handoff
 *   3. Invoke the VGCA plugin with the handoff (no second /initiate — handoff already issued)
 *   4. Return requestId so the caller can poll useSigningRequestStatus
 *
 * On Completed: invalidates the files list query so the row reflects signed state.
 * Surface loading/error states for the modal to render appropriate UI.
 */

import { useState, useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';
import { stageFileForSigning } from './file-signing-api';
import { createSigningRequest } from '@/features/digital-signature/signature-api';
import { runVgcaPlugin, signTypeToPluginFn } from '@/features/digital-signature/providers/vgca-docflow-methods';
import type { DocFlowSignTypeName } from '@/features/digital-signature/signature-types';

export interface SignUploadedFileParams {
  fileId: string;
  /** Original file name — used as FileName in VGCA stamp (sans extension as DocNumber prefill). */
  fileName: string;
  /** Active provider key from tenant config (e.g. 'usb-vgca'). */
  providerKey: string;
  /** Which VGCA sign type to invoke. Defaults to 'Issued' for ad-hoc uploaded documents. */
  signType: DocFlowSignTypeName;
  /** Document reference number shown in the visible stamp. */
  docNumber: string;
  /** Issuance date shown in the visible stamp. */
  issuedDate: Date;
}

export interface UseSignUploadedFileResult {
  /** True while any async step (stage / create / plugin) is running. */
  isLoading: boolean;
  /** Error message from any failed step, null when clean. */
  error: string | null;
  /**
   * Public id of the created signing request — defined once the signing request
   * is created. Use with useSigningRequestStatus to poll for completion.
   */
  requestId: string | undefined;
  /** Run the full stage → create → plugin flow. Idempotent per invocation. */
  startSigning: (params: SignUploadedFileParams) => Promise<void>;
  /** Clear error so the user can retry after a failure. */
  clearError: () => void;
  /**
   * Clear error AND requestId before a fresh run. Callers that re-run within the same mounted component
   * (e.g. the demo card signing a second PDF) must call this in their start handler so the previous run's
   * cached Completed status — still live in useSigningRequestStatus(requestId) — cannot re-trigger a stale
   * post-completion side effect before the new run overwrites requestId.
   */
  reset: () => void;
}

/**
 * Orchestrates the multi-step flow to sign an uploaded PDF via USB DocFlow.
 * The hook drives the async ceremony and surfaces state for the modal.
 * Poll completion via useSigningRequestStatus(requestId) in the caller.
 */
export function useSignUploadedFile(): UseSignUploadedFileResult {
  const queryClient = useQueryClient();
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [requestId, setRequestId] = useState<string | undefined>(undefined);

  const startSigning = useCallback(async (params: SignUploadedFileParams) => {
    setIsLoading(true);
    setError(null);
    setRequestId(undefined);

    try {
      // Step 1: copy file to signing bucket, mint docId
      const staged = await stageFileForSigning(params.fileId);

      // Step 2: create durable signing request — returns VGCA handoff in clientHandoff.
      // sourceFileId links the request to the Files FileRecord so signing-steps can be queried
      // via GET /files/{fileId}/signing-steps after the ceremony completes.
      const created = await createSigningRequest({
        artifactKind: 'UploadedPdf',
        artifactRef: staged.docId,
        providerKey: params.providerKey,
        sourceFileId: params.fileId,
        docFlow: {
          signType: params.signType,
          originalStorageKey: staged.originalStorageKey,
          docNumber: params.docNumber,
          issuedDate: params.issuedDate.toISOString().slice(0, 10),
          fileName: params.fileName,
        },
      });

      setRequestId(created.requestId);

      // Step 3: invoke VGCA plugin using the handoff from the signing request.
      // We do NOT call /initiate again — the unified endpoint already created the
      // signing record and issued the JWT. A second call would create a duplicate record.
      const handoff = created.clientHandoff;
      await runVgcaPlugin(
        signTypeToPluginFn(params.signType),
        {
          handlerUrl: handoff['handlerUrl'] ?? '',
          jwt:        handoff['jwt'] ?? '',
          docId:      handoff['docId'] ?? staged.docId,
        },
        {
          docNumber:  params.docNumber,
          issuedDate: params.issuedDate,
          fileName:   params.fileName,
        },
      );

      // Invalidate the files list so the row reflects signed state after poll completes.
      // Prefix invalidation (without page number) matches all pages — same pattern as
      // useUploadFile and useDeleteFile in file-api.ts.
      queryClient.invalidateQueries({ queryKey: ['files', 'list'] });
    } catch (err) {
      // Surface the backend's specific reason (e.g. "PDF is encrypted") instead of a generic toast.
      // API rejections arrive as a NormalizedApiError (a plain object, not an Error) — extractApiErrorMessage
      // reads its message; fall back to a thrown Error's message (VGCA plugin/ceremony failures carry a
      // useful reason there) and only then to the generic label.
      setError(
        extractApiErrorMessage(err) ??
        (err instanceof Error ? err.message : null) ??
        'Ký số thất bại',
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
