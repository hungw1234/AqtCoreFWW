// AI Admin API hooks — wraps /api/v1/ai/model-profiles + /api/v1/ai/prompt-templates

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface AiModelProfileDto {
  id: string;
  name: string;
  provider: string;
  modelId: string;
  isDefault: boolean;
  tokenBudget?: number;
  tokensUsed?: number;
}

export interface CreateAiModelProfileDto {
  name: string;
  provider: string;
  modelId: string;
  isDefault: boolean;
  tokenBudget?: number;
}

export interface AiPromptTemplateDto {
  id: string;
  name: string;
  description?: string;
  templateText: string;
  modelProfileId?: string;
  createdAt: string;
}

export interface CreateAiPromptTemplateDto {
  name: string;
  description?: string;
  templateText: string;
  modelProfileId?: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const aiAdminQueryKeys = {
  all: ['ai-admin'] as const,
  models: ['ai-admin', 'models'] as const,
  prompts: ['ai-admin', 'prompts'] as const,
};

// ─── Model profile queries ────────────────────────────────────────────────────

/** GET /api/v1/ai/model-profiles */
export function useAiModelProfilesAdmin() {
  return useQuery({
    queryKey: aiAdminQueryKeys.models,
    queryFn: () =>
      apiClient.get<AiModelProfileDto[]>('/ai/model-profiles').then((r) => r.data),
  });
}

/** POST /api/v1/ai/model-profiles */
export function useCreateAiModelProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAiModelProfileDto) =>
      apiClient.post<AiModelProfileDto>('/ai/model-profiles', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: aiAdminQueryKeys.models }),
  });
}

/** PUT /api/v1/ai/model-profiles/{id} */
export function useUpdateAiModelProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CreateAiModelProfileDto }) =>
      apiClient.put<AiModelProfileDto>(`/ai/model-profiles/${id}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: aiAdminQueryKeys.models }),
  });
}

/** DELETE /api/v1/ai/model-profiles/{id} */
export function useDeleteAiModelProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/ai/model-profiles/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: aiAdminQueryKeys.models }),
  });
}

// ─── Prompt template queries ──────────────────────────────────────────────────

/** GET /api/v1/ai/prompt-templates */
export function useAiPromptTemplates() {
  return useQuery({
    queryKey: aiAdminQueryKeys.prompts,
    queryFn: () =>
      apiClient.get<AiPromptTemplateDto[]>('/ai/prompt-templates').then((r) => r.data),
  });
}

/** POST /api/v1/ai/prompt-templates */
export function useCreateAiPromptTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAiPromptTemplateDto) =>
      apiClient.post<AiPromptTemplateDto>('/ai/prompt-templates', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: aiAdminQueryKeys.prompts }),
  });
}

/** PUT /api/v1/ai/prompt-templates/{id} */
export function useUpdateAiPromptTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CreateAiPromptTemplateDto }) =>
      apiClient.put<AiPromptTemplateDto>(`/ai/prompt-templates/${id}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: aiAdminQueryKeys.prompts }),
  });
}

/** DELETE /api/v1/ai/prompt-templates/{id} */
export function useDeleteAiPromptTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/ai/prompt-templates/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: aiAdminQueryKeys.prompts }),
  });
}
