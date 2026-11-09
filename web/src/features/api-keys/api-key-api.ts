import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { ApiKeyDto, CreateApiKeyRequest, CreateApiKeyResponse } from './api-key-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const apiKeyQueryKeys = {
  all: ['api-keys'] as const,
  list: () => ['api-keys', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/api-keys */
export function useApiKeys() {
  return useQuery({
    queryKey: apiKeyQueryKeys.list(),
    queryFn: () =>
      apiClient.get<ApiKeyDto[]>('/admin/api-keys').then((r) => r.data),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/api-keys — returns full key once */
export function useCreateApiKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateApiKeyRequest) =>
      apiClient
        .post<CreateApiKeyResponse>('/admin/api-keys', body)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/api-keys/{id} — revoke key */
export function useRevokeApiKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/api-keys/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
    },
  });
}
