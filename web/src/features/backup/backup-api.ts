import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

export interface BackupRecordDto {
  id: string;
  type: string;
  status: string;
  filePath: string | null;
  fileSizeBytes: number | null;
  errorMessage: string | null;
  triggeredBy: string;
  startedAt: string;
  completedAt: string | null;
}

export const backupQueryKeys = {
  list: (page: number, pageSize: number) => ['backup', 'list', page, pageSize] as const,
  detail: (id: string) => ['backup', 'detail', id] as const,
};

/** GET /api/v1/admin/backup — paginated backup history */
export function useBackupRecords(page = 1, pageSize = 9999) {
  return useQuery({
    queryKey: backupQueryKeys.list(page, pageSize),
    queryFn: () =>
      apiClient
        .get<{ items: BackupRecordDto[]; totalCount: number }>('/admin/backup', { params: { page, pageSize } })
        .then((r) => r.data),
    refetchInterval: 10_000, // Poll every 10s for pending status updates
  });
}

/** POST /api/v1/admin/backup — trigger new backup */
export function useTriggerBackup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post('/admin/backup').then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['backup'] });
    },
  });
}

/** POST /api/v1/admin/backup/restore-drill — trigger restore drill */
export function useTriggerRestoreDrill() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post('/admin/backup/restore-drill').then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['backup'] });
    },
  });
}
