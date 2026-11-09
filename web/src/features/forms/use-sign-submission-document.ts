/**
 * use-sign-submission-document.ts — orchestration hook for signing a form submission's
 * generated PDF via USB DocFlow ceremony.
 *
 * Flow (no staging step — BE handles compose+stage inside the one POST):
 *   1. POST /api/forms/submissions/{publicId}/sign-document
 *      → BE generates PDF, stages to signing-original bucket, calls orchestrator
 *      → returns { requestId, clientHandoff: { handlerUrl, jwt, docId } }
 *   2. runVgcaPlugin(pluginFnName, clientHandoff, metadata)
 *      — uses the handoff directly, does NOT call /initiate again (would create a duplicate record)
 *   3. Poll useSigningRequestStatus(requestId) in caller until Completed/Failed/Expired/timedOut
 *   4. On Completed: invalidate the submission list + detail query keys
 *
 * Low-4 carry from 2c-C: clear the held requestId on plugin-invocation failure so in-modal
 * retry re-runs the full POST → plugin flow (otherwise retry polls a stuck request forever).
 */

import { useState, useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { signSubmissionDocument } from './sign-submission-document-api';
import { runVgcaPlugin, signTypeToPluginFn } from '@/features/digital-signature/providers/vgca-docflow-methods';
import type { DocFlowSignTypeName } from '@/features/digital-signature/signature-types';

export interface SignSubmissionDocumentParams {
  /** Public id (GUID) of the form submission to sign. */
  publicId: string;
  /** Template id — used to invalidate the submission list query on completion. */
  templateId: string;
  /** Active provider key from tenant config (e.g. 'usb-vgca'). */
  providerKey: string;
  /** Which VGCA sign type to invoke (drives stamp appearance). */
  signType: DocFlowSignTypeName;
  /** Document reference number shown in the visible stamp. */
  docNumber: string;
  /** Issuance date shown in the visible stamp. */
  issuedDate: Date;
  /** Optional filename for the VGCA stamp (defaults to submission-{publicId}.pdf when omitted). */
  fileName?: string;
}

export interface UseSignSubmissionDocumentResult {
  /** True while any async step (POST / plugin invocation) is running. */
  isLoading: boolean;
  /** Error message from any failed step, null when clean. */
  error: string | null;
  /**
   * Public id of the created signing request — defined once the signing request is created.
   * Pass to useSigningRequestStatus to poll for Completed/Failed/Expired.
   */
  requestId: string | undefined;
  /** Run the full POST → plugin flow. Idempotent per invocation (requestId guards re-entry). */
  startSigning: (params: SignSubmissionDocumentParams) => Promise<void>;
  /** Clear error so the user can retry after a failure. */
  clearError: () => void;
}

/**
 * Orchestrates the multi-step flow to sign a form submission PDF via USB DocFlow.
 * Drives async ceremony and surfaces state for the modal.
 * Poll completion via useSigningRequestStatus(requestId) in the caller.
 */
export function useSignSubmissionDocument(): UseSignSubmissionDocumentResult {
  const queryClient = useQueryClient();
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [requestId, setRequestId] = useState<string | undefined>(undefined);

  const startSigning = useCallback(async (params: SignSubmissionDocumentParams) => {
    setIsLoading(true);
    setError(null);
    // Do NOT reset requestId here — if a previous attempt left a request, keep it visible
    // until the new one supersedes it (set after POST succeeds).

    let newRequestId: string | undefined;

    try {
      // Step 1: POST sign-document — BE composes PDF, stages it, issues signing request.
      // Returns requestId + VGCA client handoff (no separate staging call needed).
      const created = await signSubmissionDocument(params.publicId, {
        signType: params.signType,
        docNumber: params.docNumber,
        issuedDate: params.issuedDate.toISOString().slice(0, 10), // YYYY-MM-DD
        providerKey: params.providerKey, // BE validator requires it; resolved from tenant config
      });

      newRequestId = created.requestId;
      setRequestId(created.requestId);

      const handoff = created.clientHandoff;

      // Step 2: invoke VGCA plugin using the handoff from the signing request.
      // Do NOT call /initiate again — the unified endpoint already created the signing record
      // and issued the JWT. A second call would create a duplicate record.
      await runVgcaPlugin(
        signTypeToPluginFn(params.signType),
        {
          handlerUrl: handoff['handlerUrl'] ?? '',
          jwt:        handoff['jwt'] ?? '',
          docId:      handoff['docId'] ?? '',
        },
        {
          docNumber:  params.docNumber,
          issuedDate: params.issuedDate,
          fileName:   params.fileName ?? `submission-${params.publicId}.pdf`,
        },
      );

      // Invalidate submission list and detail so the view reflects signed state after poll.
      // Prefix invalidation covers all pages for this template.
      queryClient.invalidateQueries({ queryKey: ['forms', 'submissions', params.templateId] });
      // Also invalidate any detail query that might cache the submission individually.
      queryClient.invalidateQueries({ queryKey: ['forms', 'submission', params.publicId] });
    } catch (err) {
      // Low-4 guard: if the plugin invocation fails AFTER the signing request was created,
      // clear requestId so the modal's retry flow re-runs the full POST → plugin flow.
      // Without this, retry would find a non-null requestId, skip POST, and poll a stuck request.
      if (newRequestId) {
        setRequestId(undefined);
      }
      setError(err instanceof Error ? err.message : 'Ký số tài liệu thất bại');
    } finally {
      setIsLoading(false);
    }
  }, [queryClient]);

  const clearError = useCallback(() => setError(null), []);

  return { isLoading, error, requestId, startSigning, clearError };
}
