// form-submission-api.ts — React Query hooks for form submissions, approval, export, analytics

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import { formQueryKeys } from './form-template-api';
import type {
  FormSubmissionListItemDto,
  PagedResult,
  FormAnalyticsDto,
  FormVersionDiffDto,
  BulkActionResult,
  SubmissionFieldFilter,
  SignaturePayloadRequest,
  FormulaEvaluationResult,
} from './form-types';

/** GET /api/v1/forms/submissions?templatePublicId={guid}&status={s}&filters={json} (paginated)
 * Phase-07: query param renamed templateId → templatePublicId (FormTemplate T2 Guid AK retier).
 */
export function useFormSubmissions(
  templateId: string,
  page = 1,
  pageSize = 20,
  status?: string,
  fieldFilters?: SubmissionFieldFilter[]
) {
  const filtersJson = fieldFilters?.length
    ? JSON.stringify(fieldFilters)
    : undefined;
  return useQuery({
    queryKey: [...formQueryKeys.submissions(templateId, page), status, filtersJson] as const,
    queryFn: () =>
      apiClient
        .get<PagedResult<FormSubmissionListItemDto>>('/forms/submissions', {
          params: {
            templatePublicId: templateId, page, pageSize,
            ...(status ? { status } : {}),
            ...(filtersJson ? { filters: filtersJson } : {}),
          },
        })
        .then((r) => r.data),
    enabled: Boolean(templateId),
  });
}

/** POST /api/v1/forms/submissions/{id}/approve */
export function useApproveSubmission(templateId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      submissionId,
      comment,
    }: {
      submissionId: string;
      comment?: string;
    }) =>
      apiClient
        .post(`/forms/submissions/${submissionId}/approve`, { comment })
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['forms', 'submissions', templateId] });
    },
  });
}

/** POST /api/v1/forms/submissions/{id}/reject */
export function useRejectSubmission(templateId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      submissionId,
      comment,
    }: {
      submissionId: string;
      comment: string;
    }) =>
      apiClient
        .post(`/forms/submissions/${submissionId}/reject`, { comment })
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['forms', 'submissions', templateId] });
    },
  });
}

/** POST /api/v1/forms/submissions/bulk-approve */
export function useBulkApproveSubmissions(templateId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { ids: string[]; comment?: string }) =>
      apiClient
        .post<BulkActionResult>('/forms/submissions/bulk-approve', body)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['forms', 'submissions', templateId] });
    },
  });
}

/** POST /api/v1/forms/submissions/bulk-reject */
export function useBulkRejectSubmissions(templateId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { ids: string[]; comment: string }) =>
      apiClient
        .post<BulkActionResult>('/forms/submissions/bulk-reject', body)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['forms', 'submissions', templateId] });
    },
  });
}

/** GET /api/v1/forms/submissions/export?templatePublicId={guid} — download CSV blob.
 * Phase-07: query param renamed templateId → templatePublicId (FormTemplate T2 Guid AK retier).
 */
export function useExportSubmissions() {
  return useMutation({
    mutationFn: async (templateId: string) => {
      const response = await apiClient.get(
        `/forms/submissions/export`,
        { params: { templatePublicId: templateId }, responseType: 'blob' }
      );
      const blob = new Blob([response.data as BlobPart], { type: 'text/csv' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `submissions-${templateId}.csv`;
      anchor.click();
      URL.revokeObjectURL(url);
    },
  });
}

/** GET /api/v1/forms/submissions/{id}/export-pdf — download PDF blob */
export function useExportSubmissionPdf() {
  return useMutation({
    mutationFn: async ({ submissionId }: { submissionId: string }) => {
      const response = await apiClient.get(
        `/forms/submissions/${submissionId}/export-pdf`,
        { responseType: 'blob' }
      );
      const blob = new Blob([response.data as BlobPart], { type: 'application/pdf' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `submission-${submissionId}.pdf`;
      anchor.click();
      URL.revokeObjectURL(url);
    },
  });
}

/** POST /api/v1/forms/submissions — authenticated submission.
 * Phase-07: body field renamed formTemplateId → formTemplatePublicId (Guid AK, FormTemplate T2 retier).
 */
export function useSubmitForm() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      formTemplatePublicId: string;
      data: Record<string, unknown>;
      consentGiven?: boolean;
      signature?: SignaturePayloadRequest | null;
    }) =>
      apiClient.post<{ id: string }>('/forms/submissions', body).then((r) => r.data),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: ['forms', 'submissions', vars.formTemplatePublicId] });
    },
  });
}

/** POST /api/v1/forms/templates/{publicId}/evaluate-formulas — authenticated batch formula compute.
 * Returns { results: { fieldKey: computedValue } } for every active Formula field of the template.
 * Display-only: BE re-computes at submit-time, so the FE never trusts this for persistence.
 */
export function useEvaluateFormulas() {
  return useMutation({
    mutationFn: ({
      templatePublicId,
      values,
    }: {
      templatePublicId: string;
      values: Record<string, unknown>;
    }) =>
      apiClient
        .post<FormulaEvaluationResult>(
          `/forms/templates/${templatePublicId}/evaluate-formulas`,
          { values }
        )
        .then((r) => r.data),
  });
}

/** GET /api/v1/forms/templates/{id}/analytics */
export function useFormAnalytics(id: string) {
  return useQuery({
    queryKey: ['forms', 'analytics', id] as const,
    queryFn: () =>
      apiClient
        .get<FormAnalyticsDto>(`/forms/templates/${id}/analytics`)
        .then((r) => r.data),
    enabled: Boolean(id),
  });
}

/** GET /api/v1/forms/templates/{id}/diff?fromVersion=x&toVersion=y */
export function useFormVersionDiff(
  id: string,
  fromVersion: number,
  toVersion: number,
  enabled = true
) {
  return useQuery({
    queryKey: ['forms', 'diff', id, fromVersion, toVersion] as const,
    queryFn: () =>
      apiClient
        .get<FormVersionDiffDto>(`/forms/templates/${id}/diff`, {
          params: { fromVersion, toVersion },
        })
        .then((r) => r.data),
    enabled: enabled && Boolean(id) && fromVersion > 0 && toVersion > 0,
  });
}
