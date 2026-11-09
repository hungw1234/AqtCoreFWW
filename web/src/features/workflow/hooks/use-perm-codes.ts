// Plan 260523-1241 Phase 06b — hook over orval-generated GET /api/v1/security/perm-codes.
//
// Returns a flat string[] of PermCode constants for the ByPermission clause editor.
// staleTime: Infinity — perm-codes mutate only at code-deploy.
//
// IMPORTANT runtime vs type gap (precedent: `business-process-admin-api.ts:96`):
//   The axios response interceptor at `core/api/api-client.ts:138-142` strips the
//   `{ success, data, meta, errors }` envelope BEFORE returning to orval. So at
//   runtime `query.data` is already the inner `string[]`, NOT the envelope despite
//   what the orval-generated TYPE claims (orval reads the OpenAPI envelope shape).
//
// Override the generic to `<string[]>` so the type matches runtime, otherwise we'd
// be coding against a phantom envelope that never appears at runtime.

import { useGetApiV1SecurityPermCodes } from '@/api/generated/security/security';

export interface UseWorkflowPermCodesResult {
  codes: string[];
  isLoading: boolean;
  isError: boolean;
}

export function useWorkflowPermCodes(): UseWorkflowPermCodesResult {
  const query = useGetApiV1SecurityPermCodes<string[]>({
    query: { staleTime: Infinity },
  });

  return {
    codes: query.data ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
