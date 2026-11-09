import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult } from '@/shared/types/api';

// ─── Types ───────────────────────────────────────────────────────────────────

export interface CredentialPolicyDto {
  id: string;
  name: string;
  tenantId?: string;
  minLength: number;
  maxLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireDigit: boolean;
  requireSpecialChar: boolean;
  rotationDays: number;           // days before password must be rotated (was maxAge)
  maxFailedAttempts: number;      // login failures before lockout (was lockoutThreshold)
  lockoutMinutes: number;         // duration of account lockout in minutes (was lockoutDuration)
  passwordHistoryCount: number;   // number of previous passwords to remember (was historyCount)
  isDefault: boolean;
  // QĐ742 1.4.d — escalation fields (3 lockout cycles in window → account auto-disabled)
  escalationLockoutCount: number; // consecutive lockouts before permanent disable (default 3)
  escalationWindowHours: number;  // rolling window in hours for counting lockouts (default 24)
  showRemainingAttempts: boolean; // show remaining login attempts on failure when ≤ 2 left
}

export interface CreateCredentialPolicyDto {
  name: string;
  minLength: number;
  maxLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireDigit: boolean;
  requireSpecialChar: boolean;
  rotationDays: number;
  maxFailedAttempts: number;
  lockoutMinutes: number;
  passwordHistoryCount: number;
  isDefault: boolean;
  // QĐ742 1.4.d — escalation fields
  escalationLockoutCount: number;
  escalationWindowHours: number;
  showRemainingAttempts: boolean;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const credentialPoliciesQueryKeys = {
  all: ['credential-policies'] as const,
  list: ['credential-policies', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/credential-policies — list all password/lockout policies */
export function useCredentialPolicies() {
  return useQuery({
    queryKey: credentialPoliciesQueryKeys.list,
    queryFn: () =>
      apiClient
        .get<PaginatedResult<CredentialPolicyDto>>('/admin/credential-policies')
        .then((r) => r.data),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/credential-policies — create a new credential policy */
export function useCreateCredentialPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateCredentialPolicyDto) =>
      apiClient
        .post<CredentialPolicyDto>('/admin/credential-policies', dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: credentialPoliciesQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/credential-policies/{id} — update an existing policy */
export function useUpdateCredentialPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: CreateCredentialPolicyDto & { id: string }) =>
      apiClient
        .put<CredentialPolicyDto>(`/admin/credential-policies/${id}`, dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: credentialPoliciesQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/credential-policies/{id} — remove a policy */
export function useDeleteCredentialPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/credential-policies/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: credentialPoliciesQueryKeys.all });
    },
  });
}
