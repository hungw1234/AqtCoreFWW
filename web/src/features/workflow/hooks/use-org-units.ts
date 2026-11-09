// RT2-15 Phase 04 — hook for GET /api/v1/admin/org/units (full tree).
//
// Hand-written (orval regen requires running backend; not available at build time).
// Mirrors use-user-groups.ts pattern; uses the same admin endpoint that org-api.ts uses.
//
// Auth: endpoint requires org-unit admin access — callers are workflow definition editors
// who already hold WORKFLOW.DEF.MANAGE; the admin org/units endpoint is accessible
// to all authenticated users with sufficient tenant scope.
//
// BE controller does not bind activeOnly param → returns full tree.
// Active-only filtering is done client-side in WorkflowOrgUnitMultiSelect.
// staleTime: 5 min — org unit list changes rarely during a single edit session.

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

export interface OrgUnitSummary {
  publicId: string;
  code: string;
  name: string;
  isActive: boolean;
}

export const orgUnitsQueryKeys = {
  list: (activeOnly: boolean) => ['org-units', 'workflow-picker', { activeOnly }] as const,
};

export interface UseWorkflowOrgUnitsResult {
  data: OrgUnitSummary[] | undefined;
  isLoading: boolean;
  error: unknown;
}

// Distinct from features/organization/org-api.ts useOrgUnits (no staleTime, no activeOnly filter).
// This workflow-scoped variant: activeOnly=true by default, 5-min staleTime, dedicated query key.
export function useWorkflowOrgUnits(opts?: { activeOnly?: boolean; background?: boolean }): UseWorkflowOrgUnitsResult {
  const activeOnly = opts?.activeOnly ?? true;

  const query = useQuery({
    queryKey: orgUnitsQueryKeys.list(activeOnly),
    queryFn: () =>
      apiClient
        // background: cosmetic label lookup — a stale-MFA 403 here must not bounce the
        // whole page to the 2FA enrolment screen; degrade to raw org-unit codes instead.
        .get<OrgUnitSummary[]>('/admin/org/units', opts?.background ? { suppressAuthInterstitial: true } : undefined)
        .then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    // background: also silence the global load-failed toast (api-client only suppresses
    // the axios-layer redirect/toast; the react-query QueryCache.onError is separate).
    ...(opts?.background ? { meta: { suppressErrorToast: true } } : {}),
  });

  return {
    data: query.data,
    isLoading: query.isLoading,
    error: query.error,
  };
}
