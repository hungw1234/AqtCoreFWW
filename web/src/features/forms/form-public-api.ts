// form-public-api.ts — React Query hooks for public forms + workflow definitions

import axios from 'axios';
import { useQuery, useMutation } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { FormTemplateDto, FormulaEvaluationResult, DataSourceOptionDto } from './form-types';

// Plain axios instance for anonymous/public endpoints — does NOT attach Bearer tokens or
// trigger userManager.getUser(). This prevents auth errors for unauthenticated form visitors.
const publicClient = axios.create({
  baseURL: (import.meta.env.VITE_API_BASE_URL as string) || '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

// Unwrap ApiResponse envelope — same pattern as apiClient interceptor
publicClient.interceptors.response.use((response) => {
  const envelope = response.data as { success?: boolean; data?: unknown };
  if (envelope && typeof envelope === 'object' && 'success' in envelope) {
    response.data = envelope.data;
  }
  return response;
});

/** GET /api/v1/public/forms/{code} — anonymous form schema fetch */
export function usePublicForm(code: string) {
  return useQuery({
    queryKey: ['public-form', code] as const,
    queryFn: () =>
      publicClient.get<FormTemplateDto>(`/public/forms/${code}`).then((r) => r.data),
    enabled: Boolean(code),
  });
}

/** POST /api/v1/public/forms/{code}/submit — anonymous submission */
export function useSubmitPublicForm(code: string) {
  return useMutation({
    mutationFn: ({ data, consentGiven }: { data: Record<string, unknown>; consentGiven?: boolean }) =>
      publicClient
        .post<{ id: string }>(`/public/forms/${code}/submit`, {
          data,
          ...(consentGiven !== undefined ? { consentGiven } : {}),
        })
        .then((r) => r.data),
  });
}

/** POST /api/v1/public/forms/{code}/evaluate-formulas — anonymous batch formula compute.
 * Body `{ data }` (draft values); returns { results: { fieldKey: computedValue } } for active
 * Formula fields. Display-only: BE re-computes at submit-time. Rate-limited (`formula-evaluate`).
 */
export function useEvaluatePublicFormulas(code: string) {
  return useMutation({
    mutationFn: (data: Record<string, unknown>) =>
      publicClient
        .post<FormulaEvaluationResult>(`/public/forms/${code}/evaluate-formulas`, { data })
        .then((r) => r.data),
  });
}

/**
 * GET /api/v1/public/forms/{code}/fields/{fieldKey}/options — anonymous cascade option resolve.
 *
 * The public (anonymous) mirror of the authed `useFieldOptions` (`form-template-api.ts`). A public form
 * addresses a reference field by the form's `code` + the field's stable `fieldKey` (never the internal
 * PK), so an anonymous visitor never needs a template-id or a Bearer token. Cascade v1: `parentValues`
 * ({ filterParamName: value }) are sent as `?p[filterParamName]=value` and included in the query key, so
 * a parent change refetches automatically. `enabled` gates the fetch when the caller's cascade parents
 * are not all set yet — avoids firing an unfiltered request that would leak the full list before the
 * user picks the parent (symmetry with the authed hook's `enabled` arg).
 */
export function usePublicFieldOptions(
  code: string,
  fieldKey: string,
  parentValues?: Record<string, string>,
  enabled = true,
) {
  const hasParents = Boolean(parentValues && Object.keys(parentValues).length > 0);
  return useQuery({
    // parentValues in the key ⇒ each distinct parent selection caches its own filtered option-list, and a
    // parent change (new key) drives an automatic refetch. Absent parentValues ⇒ the shorter key matches
    // the pre-cascade (static) form — no cache churn on non-cascade reference fields.
    queryKey: hasParents
      ? (['public-field-options', code, fieldKey, parentValues] as const)
      : (['public-field-options', code, fieldKey] as const),
    queryFn: () =>
      publicClient
        .get<DataSourceOptionDto[]>(`/public/forms/${code}/fields/${fieldKey}/options`, {
          params: hasParents
            ? Object.fromEntries(
                Object.entries(parentValues!).map(([k, v]) => [`p[${k}]`, v]),
              )
            : undefined,
        })
        .then((r) => r.data),
    enabled: Boolean(code && fieldKey) && enabled,
    staleTime: 5 * 60 * 1000,
  });
}

/** GET /api/v1/workflow/definitions?isActive=true — list active workflow definitions */
export function useWorkflowDefinitions() {
  return useQuery({
    queryKey: ['workflow', 'definitions'] as const,
    queryFn: () =>
      apiClient
        .get('/workflow/definitions', { params: { isActive: true, page: 1, pageSize: 100 } })
        .then((r) => r.data),
    staleTime: 5 * 60 * 1000,
  });
}
