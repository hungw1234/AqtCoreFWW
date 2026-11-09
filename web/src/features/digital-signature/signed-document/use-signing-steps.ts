/**
 * use-signing-steps.ts — TanStack Query hook for signing-step rows.
 *
 * Returns an ordered list of SigningStepRow (sequenceNumber ascending) for a
 * given (scope, id) pair. Empty array when no signing exists — not an error.
 * Enabled only when `id` is non-empty to avoid spurious requests on unmount.
 */

import { useQuery } from '@tanstack/react-query';
import { fetchSigningSteps } from './signed-document-api';
import type { SignedDocumentScope } from './signing-step-types';

export const signingStepsQueryKeys = {
  steps: (scope: SignedDocumentScope, id: string) =>
    ['signing-steps', scope, id] as const,
};

/**
 * Query hook that returns signing-step rows for a specific artifact.
 * `scope` + `id` together identify the artifact (file or form submission).
 */
export function useSigningSteps(scope: SignedDocumentScope, id: string) {
  return useQuery({
    queryKey: signingStepsQueryKeys.steps(scope, id),
    queryFn: () => fetchSigningSteps(scope, id),
    enabled: Boolean(id),
    // Signing history is relatively stable; 30s stale time avoids refetch on tab-focus while
    // the user browses between list and detail. Invalidated by the sign mutation's onSuccess.
    staleTime: 30 * 1000,
  });
}
