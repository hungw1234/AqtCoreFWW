// use-prefill-context.ts — TanStack Query hook for BP Run prefill context (P6 endpoint)
// Contract: returns {data, isLoading, isError} where data is undefined while loading,
// {} when BE returns empty dict, field-value dict otherwise.

import { useMemo } from 'react';
import { usePrefillContextRaw } from './business-process-run-api';

/**
 * Resolves prefill values for a form rendered inside a BP Run.
 * Memoized to avoid re-mount on background refetch (stable reference).
 *
 * @param workflowInstanceId - Guid string of the workflow instance
 * @param formTemplatePublicId - PublicId (Guid string) of the form template
 */
export function usePrefillContext(
  workflowInstanceId: string,
  formTemplatePublicId: string,
): {
  data: Record<string, unknown> | undefined;
  isLoading: boolean;
  isError: boolean;
} {
  const enabled = !!workflowInstanceId && !!formTemplatePublicId;
  const { data: raw, isLoading, isError } = usePrefillContextRaw(
    workflowInstanceId,
    formTemplatePublicId,
    enabled,
  );

  // Memoize so consumers don't see a new object reference on every render
  const data = useMemo<Record<string, unknown> | undefined>(() => {
    if (isLoading) return undefined;
    if (!raw) return {};
    return raw as Record<string, unknown>;
  }, [raw, isLoading]);

  return { data, isLoading, isError };
}
