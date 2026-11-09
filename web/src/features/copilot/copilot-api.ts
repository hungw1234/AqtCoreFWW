// Copilot API hooks — wraps /api/v1/ai/copilot/* and /api/v1/ai/models endpoints

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import { userManager } from '@/features/auth/auth-provider';
import { cleanupAndRedirect } from '@/features/auth/session-error-handler';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface AiModelProfile {
  id: string;
  name: string;
  provider: string;
  modelId: string;
  isDefault: boolean;
}

export interface CopilotMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
}

export interface SendMessageRequest {
  sessionId?: string;
  message: string;
  modelProfileId?: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const copilotQueryKeys = {
  models: ['copilot', 'models'] as const,
};

// ─── Queries ──────────────────────────────────────────────────────────────────

/** GET /api/v1/ai/model-profiles — list available AI model profiles */
export function useAiModelProfiles() {
  return useQuery({
    queryKey: copilotQueryKeys.models,
    queryFn: () =>
      apiClient.get<AiModelProfile[]>('/ai/model-profiles').then((r) => r.data),
  });
}

// ─── SSE streaming helper ─────────────────────────────────────────────────────

/**
 * streamCopilotMessage — opens SSE connection to /api/v1/ai/copilot/stream
 * Calls onToken for each chunk, onDone when stream ends, onError on failure.
 * Returns cleanup function to abort the stream.
 */
export function streamCopilotMessage(
  body: SendMessageRequest,
  onToken: (token: string) => void,
  onDone: () => void,
  onError: (err: Error) => void,
): () => void {
  const controller = new AbortController();

  (async () => {
    try {
      const baseUrl = (import.meta.env.VITE_API_BASE_URL as string) || '/api/v1';

      // Attach auth token + tenant override + correlation ID (matches api-client interceptor)
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      const user = await userManager.getUser().catch(() => null);
      if (user?.access_token && !user.expired) {
        headers['Authorization'] = `Bearer ${user.access_token}`;
      }
      const tenantOverride = sessionStorage.getItem('admin-tenant-override');
      if (tenantOverride && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(tenantOverride))
        headers['X-Tenant-Id'] = tenantOverride;
      headers['X-Correlation-Id'] = crypto.randomUUID();

      const response = await fetch(`${baseUrl}/ai/copilot/stream`, {
        method: 'POST',
        headers,
        body: JSON.stringify(body),
        signal: controller.signal,
      });

      if (!response.ok) {
        // SSE bypasses api-client 401 retry — delegate to unified helper.
        if (response.status === 401) {
          await cleanupAndRedirect();
        }
        throw new Error(`HTTP ${response.status}`);
      }

      const reader = response.body?.getReader();
      if (!reader) throw new Error('No response body');

      const decoder = new TextDecoder();

      // Read SSE stream — each line is "data: <token>" or "data: [DONE]"
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        const lines = chunk.split('\n');

        for (const line of lines) {
          if (!line.startsWith('data:')) continue;
          const payload = line.slice(5).trim();
          if (payload === '[DONE]') {
            onDone();
            return;
          }
          if (payload) onToken(payload);
        }
      }
      onDone();
    } catch (err) {
      if ((err as Error).name !== 'AbortError') {
        onError(err instanceof Error ? err : new Error(String(err)));
      }
    }
  })();

  return () => controller.abort();
}
