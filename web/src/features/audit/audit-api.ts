import { useMutation, useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult } from '@/shared/types/api';
import type {
  AuditLogEntry,
  AuditLogParams,
  AuditStatistics,
  LoginAuditEntry,
  LoginAuditParams,
  SecurityIncidentEntry,
  SecurityIncidentParams,
} from './audit-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const auditQueryKeys = {
  all: ['audit'] as const,
  logs: (params: AuditLogParams) => ['audit', 'logs', params] as const,
  statistics: () => ['audit', 'statistics'] as const,
  loginAudit: (params: LoginAuditParams) => ['audit', 'login', params] as const,
  incidents: (params: SecurityIncidentParams) => ['audit', 'incidents', params] as const,
};

// ─── Hooks ───────────────────────────────────────────────────────────────────

/**
 * Fetch paginated audit logs.
 * GET /api/v1/audit/logs
 */
export function useAuditLogs(params: AuditLogParams) {
  return useQuery({
    queryKey: auditQueryKeys.logs(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<AuditLogEntry>>('/audit/logs', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev, // keep previous page visible during refetch
  });
}

/**
 * Fetch aggregated audit statistics.
 * GET /api/v1/audit/statistics
 */
export function useAuditStatistics() {
  return useQuery({
    queryKey: auditQueryKeys.statistics(),
    queryFn: () =>
      apiClient
        .get<AuditStatistics>('/audit/statistics')
        .then((r) => r.data),
  });
}

/**
 * Export audit logs as CSV and trigger browser download.
 * GET /api/v1/audit/logs/export — returns text/csv blob (max 10,000 rows).
 *
 * Usage: const { mutate: exportCsv, isPending } = useExportAuditLogs();
 *        exportCsv({ from: '2024-01-01', to: '2024-12-31' });
 */
export function useExportAuditLogs() {
  return useMutation({
    mutationFn: async (params: Omit<AuditLogParams, 'page' | 'pageSize'>) => {
      const response = await apiClient.get('/audit/logs/export', {
        params,
        responseType: 'blob',
      });

      // Extract filename from Content-Disposition header, fallback to timestamp
      const disposition = response.headers['content-disposition'] as string | undefined;
      const match = disposition?.match(/filename[^;=\n]*=(['"]?)([^'";\n]+)\1/);
      const filename = match?.[2] ?? `audit-logs-${new Date().toISOString().slice(0, 10)}.csv`;

      // Trigger download without opening a new tab
      const url = URL.createObjectURL(new Blob([response.data as BlobPart], { type: 'text/csv;charset=utf-8;' }));
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = filename;
      anchor.click();
      URL.revokeObjectURL(url);
    },
  });
}

/**
 * Fetch paginated login audit logs.
 * GET /api/v1/admin/login-audit
 */
export function useLoginAudit(params: LoginAuditParams) {
  return useQuery({
    queryKey: auditQueryKeys.loginAudit(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<LoginAuditEntry>>('/admin/login-audit', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

/**
 * Verify HMAC integrity chain of all audit log entries.
 * GET /api/v1/audit/logs/verify-chain — returns { isValid: boolean, brokenAt?: string }
 */
export function useVerifyAuditChain() {
  return useMutation({
    mutationFn: () =>
      apiClient
        .get<{ isValid: boolean; brokenAt?: string }>('/audit/logs/verify-chain')
        .then((r) => r.data),
  });
}

/**
 * Fetch paginated security incidents.
 * GET /api/v1/admin/incidents
 */
export function useSecurityIncidents(params: SecurityIncidentParams) {
  return useQuery({
    queryKey: auditQueryKeys.incidents(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<SecurityIncidentEntry>>('/admin/incidents', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}
