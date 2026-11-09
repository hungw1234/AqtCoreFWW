// AI API hooks — wraps /api/v1/ai/* endpoints

import { useQuery, useMutation } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { ExecuteQueryResponse } from './ai-types';

export const aiQueryKeys = {
  history: (sessionId: string) => ['ai', 'history', sessionId] as const,
};

/** GET /api/v1/ai/chat/{sessionId}/history — chat history */
export function useChatHistory(sessionId: string, page = 1) {
  return useQuery({
    queryKey: aiQueryKeys.history(sessionId),
    queryFn: () =>
      apiClient
        .get(`/ai/chat/${sessionId}/history`, { params: { page, pageSize: 20 } })
        .then((r) => r.data),
    enabled: Boolean(sessionId),
  });
}

/** POST /api/v1/ai/chat — send message, get full response */
export function useAiChat() {
  return useMutation({
    mutationFn: (body: { sessionId?: string; message: string }) =>
      apiClient
        .post<ExecuteQueryResponse>('/ai/chat', body)
        .then((r) => r.data),
  });
}

/** POST /api/v1/ai/chat (NLQ mode) — natural language query */
export function useNlqQuery() {
  return useMutation({
    mutationFn: (question: string) =>
      apiClient
        .post<ExecuteQueryResponse>('/ai/chat', { message: question })
        .then((r) => r.data),
  });
}
