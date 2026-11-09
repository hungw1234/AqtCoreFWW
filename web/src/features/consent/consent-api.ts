// Consent API hooks — React Query wrappers for PDPL consent endpoints (user-facing)

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface ConsentRecordDto {
  id: string;
  dataSubjectId: string;
  purpose: string;
  legalBasis: string;
  dataSubjectType?: string;
  evidenceJson?: string;
  isWithdrawn: boolean;
  withdrawnAt?: string;
  expiresAt?: string;
  createdAtUtc: string;
  tenantId?: string;
}

export interface WithdrawConsentRequest {
  purpose: string;
  scope?: string;
}

// ─── Query Keys ─────────────────────────────────────────────────────────────

export const consentQueryKeys = {
  list: () => ['consents'] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

/** GET /api/v1/identity/consents — list current user's consent records */
export function useConsents() {
  return useQuery({
    queryKey: consentQueryKeys.list(),
    queryFn: () =>
      apiClient
        .get<{ items: ConsentRecordDto[] }>('/identity/consents')
        .then((r) => r.data.items),
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** POST /api/v1/identity/consents/withdraw — withdraw consent for a given purpose */
export function useWithdrawConsent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (dto: WithdrawConsentRequest) =>
      apiClient.post('/identity/consents/withdraw', dto).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['consents'] }),
  });
}
