import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  SodViolationLogsPagedResult,
  SodViolationsFilters,
} from './sod-violations-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const sodViolationsQueryKeys = {
  all: ['sod-violations'] as const,
  list: (filters: SodViolationsFilters, page: number, pageSize: number) =>
    ['sod-violations', 'list', filters, page, pageSize] as const,
};

// ─── Export function ─────────────────────────────────────────────────────────

/** Build query string from filters */
function buildQueryString(filters: SodViolationsFilters): string {
  const params = new URLSearchParams();
  if (filters.userId) params.append('userId', filters.userId);
  if (filters.roleId) params.append('roleId', filters.roleId);
  if (filters.ruleId) params.append('ruleId', filters.ruleId);
  if (filters.from) params.append('from', filters.from);
  if (filters.to) params.append('to', filters.to);
  return params.toString();
}

/**
 * Export SoD violations as CSV (streaming download).
 * Returns Blob for browser download. Requires IDENTITY.SOD.EXPORT_AUDIT_LOG permission.
 * Uses apiClient so Bearer token + 401 retry/redirect interceptor are wired in.
 */
export async function exportSodViolations(filters: SodViolationsFilters): Promise<Blob> {
  const qs = buildQueryString(filters);
  const path = `/admin/sod-rules/violations/export${qs ? `?${qs}` : ''}`;
  const res = await apiClient.get<Blob>(path, { responseType: 'blob' });
  return res.data;
}

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/sod-rules/violations — list SoD violation audit logs */
export function useSodViolations(
  filters: SodViolationsFilters,
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: sodViolationsQueryKeys.list(filters, page, pageSize),
    queryFn: async () => {
      const params = new URLSearchParams();
      if (filters.userId) params.append('userId', filters.userId);
      if (filters.roleId) params.append('roleId', filters.roleId);
      if (filters.ruleId) params.append('ruleId', filters.ruleId);
      if (filters.from) params.append('from', filters.from);
      if (filters.to) params.append('to', filters.to);
      params.append('page', String(page));
      params.append('pageSize', String(pageSize));

      const res = await apiClient.get<SodViolationLogsPagedResult>(
        `/admin/sod-rules/violations?${params.toString()}`,
      );
      return res.data;
    },
  });
}
