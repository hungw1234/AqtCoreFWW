import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ────────────────────────────────────────────────────────────────────

// BackupRecordDto — matches BackupAdminController inline DTO
// No BackupPolicy/schedule entity exists on BE; runs are ad-hoc or Hangfire cron-triggered.
export interface BackupRunDto {
  id: string; // Guid
  type: 'Backup' | 'RestoreDrill' | string;
  status: 'Pending' | 'InProgress' | 'Completed' | 'Failed' | string;
  filePath?: string;
  fileSizeBytes?: number;
  errorMessage?: string;
  triggeredBy: string; // Guid of user who triggered
  startedAt: string;   // ISO DateTimeOffset
  completedAt?: string;
}

export interface BackupListResult {
  items: BackupRunDto[];
  totalCount: number;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const backupPolicyQueryKeys = {
  all: ['backup-policy'] as const,
  list: (page: number, pageSize: number) =>
    ['backup-policy', 'list', page, pageSize] as const,
  detail: (id: string) => ['backup-policy', 'detail', id] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/backup?page=N&pageSize=N — paginated backup run history */
export function useBackupRuns(page = 1, pageSize = 50) {
  return useQuery({
    queryKey: backupPolicyQueryKeys.list(page, pageSize),
    queryFn: () =>
      apiClient
        .get<BackupListResult>('/admin/backup', { params: { page, pageSize } })
        .then((r) => r.data),
    refetchInterval: 15_000, // poll while runs may be in-progress
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/admin/backup — trigger a new database backup */
export function useTriggerBackupRun() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiClient.post<{ recordId: string; status: string }>('/admin/backup').then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: backupPolicyQueryKeys.all });
    },
  });
}

/** POST /api/v1/admin/backup/restore-drill — trigger restore drill (backup→restore→verify→cleanup) */
export function useTriggerRestoreDrill() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiClient.post<{ recordId: string; status: string }>('/admin/backup/restore-drill').then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: backupPolicyQueryKeys.all });
    },
  });
}
