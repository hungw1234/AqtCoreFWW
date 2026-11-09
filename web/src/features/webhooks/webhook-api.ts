import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface WebhookSubscriptionDto {
  id: string;
  endpointUrl: string;
  eventTypes: string[];
  isActive: boolean;
  createdAt: string;
}

export interface WebhookDeliveryDto {
  id: string;
  subscriptionId: string;
  eventType: string;
  attemptNumber: number;
  statusCode: number | null;
  errorMessage: string | null;
  attemptedAt: string;
  isSuccess: boolean;
}

// ─── Query keys ─────────────────────────────────────────────────────────────

export const webhookQueryKeys = {
  subscriptions: ['webhooks', 'subscriptions'] as const,
  deliveries: (subId: string, page: number) => ['webhooks', 'deliveries', subId, page] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/webhooks — list active subscriptions */
export function useWebhookSubscriptions() {
  return useQuery({
    queryKey: webhookQueryKeys.subscriptions,
    queryFn: () =>
      apiClient.get<WebhookSubscriptionDto[]>('/admin/webhooks').then((r) => r.data),
  });
}

/** GET /api/v1/admin/webhooks/{id}/deliveries — paginated delivery attempts */
export function useWebhookDeliveries(subscriptionId: string, page = 1, pageSize = 20) {
  return useQuery({
    queryKey: webhookQueryKeys.deliveries(subscriptionId, page),
    queryFn: () =>
      apiClient
        .get<WebhookDeliveryDto[]>(`/admin/webhooks/${subscriptionId}/deliveries`, {
          params: { page, pageSize },
        })
        .then((r) => r.data),
    enabled: Boolean(subscriptionId),
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** POST /api/v1/admin/webhooks/{id}/test — send test ping */
export function useTestWebhook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/admin/webhooks/${id}/test`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhooks'] });
    },
  });
}
