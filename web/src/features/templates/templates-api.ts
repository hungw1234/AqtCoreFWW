// Templates API hooks — wraps /api/v1/document-templates/* endpoints

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult, PaginationParams } from '@/shared/types/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export type DocumentOutputFormat = 'Pdf' | 'Docx' | 'Html';
export type DocumentTemplateStatus = 'Draft' | 'Active' | 'Archived';

export interface DocumentTemplateDto {
  id: string;
  name: string;
  code: string;
  description?: string;
  outputFormat: DocumentOutputFormat;
  templateContent: string;
  status: DocumentTemplateStatus;
  createdAt: string;
}

export interface CreateTemplateDto {
  name: string;
  code: string;
  description?: string;
  outputFormat: DocumentOutputFormat;
  templateContent: string;
}

export interface UpdateTemplateDto {
  name: string;
  description?: string;
  templateContent: string;
}

export interface GeneratePreviewRequest {
  templateId: string;
  dataJson: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const templatesQueryKeys = {
  all: ['templates'] as const,
  list: (params: PaginationParams) => ['templates', 'list', params] as const,
  detail: (id: string) => ['templates', 'detail', id] as const,
};

// ─── Queries ──────────────────────────────────────────────────────────────────

/** GET /api/v1/document-templates — paginated list */
export function useTemplates(params: PaginationParams = {}) {
  return useQuery({
    queryKey: templatesQueryKeys.list(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<DocumentTemplateDto>>('/document-templates', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

/** GET /api/v1/document-templates/{id} */
export function useTemplate(id: string) {
  return useQuery({
    queryKey: templatesQueryKeys.detail(id),
    queryFn: () =>
      apiClient.get<DocumentTemplateDto>(`/document-templates/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/document-templates */
export function useCreateTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateTemplateDto) =>
      apiClient.post<DocumentTemplateDto>('/document-templates', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: templatesQueryKeys.all }),
  });
}

/** PUT /api/v1/document-templates/{id} */
export function useUpdateTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateTemplateDto }) =>
      apiClient.put<DocumentTemplateDto>(`/document-templates/${id}`, body).then((r) => r.data),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: templatesQueryKeys.detail(id) });
      qc.invalidateQueries({ queryKey: templatesQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/document-templates/{id} */
export function useDeleteTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/document-templates/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: templatesQueryKeys.all }),
  });
}

/** POST /api/v1/document-templates/preview — generate and download rendered output */
export function useGeneratePreview() {
  return useMutation({
    mutationFn: (body: GeneratePreviewRequest) =>
      apiClient
        .post('/document-templates/preview', body, { responseType: 'blob' })
        .then((r) => r.data as Blob),
  });
}
