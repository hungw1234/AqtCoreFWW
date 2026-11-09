import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ──────────────────────────────────────────────────────────────────

// Lifecycle status name — mirrors BE DelegationStatus enum (serialized as the name).
// PendingApproval = created on behalf of another user, awaiting delegator/SystemAdmin approval.
export type DelegationStatus = 'PendingApproval' | 'Active' | 'Expired' | 'Revoked';

// Mirrors BE DelegationDto record. `status` is the authoritative lifecycle state from the BE;
// deriveIsActive() remains for the time-window check, but the badge renders by `status`.
export interface DelegationDto {
  id: string;
  delegatorId: string;
  delegatorName?: string | null;
  delegateId: string;
  delegateName?: string | null;
  validFrom: string;
  validTo: string;
  reason: string | null;
  isRevoked: boolean;
  revokedAt: string | null;
  status: DelegationStatus;
}

export interface CreateDelegationDto {
  delegatorId: string;
  delegateId: string;
  validFrom: string;
  validTo: string;
  reason?: string;
}

// Matches BE PagedResult<T> shape returned by ListDelegationsQueryHandler.
// Offset-paginated endpoints carry totalCount; meta holds page/cursor info.
export interface PagedResult<T> {
  items: T[];
  totalCount: number | null;
  meta: {
    page: number | null;
    pageSize: number | null;
    totalPages: number | null;
    hasNextPage: boolean;
  };
}

// Derive UI active state from raw BE fields. Active = not revoked AND now within [validFrom, validTo].
// Pure helper — kept here so all consumers (page, tests, future widgets) share the same definition.
export function deriveIsActive(d: Pick<DelegationDto, 'isRevoked' | 'validFrom' | 'validTo'>, now: Date = new Date()): boolean {
  if (d.isRevoked) return false;
  const ts = now.getTime();
  return new Date(d.validFrom).getTime() <= ts && ts <= new Date(d.validTo).getTime();
}

// ─── Query keys ─────────────────────────────────────────────────────────────

export const delegationQueryKeys = {
  list: (activeOnly: boolean) => ['delegations', activeOnly] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

/** GET /api/v1/delegations?activeOnly=true — list delegations (paged) */
export function useDelegations(activeOnly = true) {
  return useQuery({
    queryKey: delegationQueryKeys.list(activeOnly),
    queryFn: () =>
      apiClient
        .get<PagedResult<DelegationDto>>('/delegations', { params: { activeOnly } })
        .then((r) => r.data),
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** POST /api/v1/delegations — create a delegation */
export function useCreateDelegation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateDelegationDto) =>
      apiClient.post<DelegationDto>('/delegations', dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['delegations'] });
    },
  });
}

/** DELETE /api/v1/delegations/{id} — revoke a delegation */
export function useRevokeDelegation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/delegations/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['delegations'] });
    },
  });
}

/** POST /api/v1/delegations/{id}/approve — approve a PendingApproval delegation (delegator or SystemAdmin) */
export function useApproveDelegation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/delegations/${id}/approve`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['delegations'] });
    },
  });
}
