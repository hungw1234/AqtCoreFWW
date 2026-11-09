// Workflow migration API hooks — React Query wrappers for version migration endpoints (T5-10)
// Separated from workflow-api.ts to keep files under 200-line limit.

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  VersionHistoryDto,
  MigrationDto,
  MigrationItemDto,
  MigrationValidationResult,
  CreateMigrationRequest,
  MigrationItemStatus,
} from './workflow-types';

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ─── Query Keys ────────────────────────────────────────────────────────────────

export const migrationKeys = {
  versionHistory: (defKey: string) => ['workflow', 'version-history', defKey] as const,
  migrationsByDef: (defKey: string) => ['workflow', 'migrations-by-def', defKey] as const,
  migration: (id: string) => ['workflow', 'migration', id] as const,
  migrationItems: (id: string, page: number, status?: MigrationItemStatus) =>
    ['workflow', 'migration-items', id, page, status] as const,
};

// ─── Version History ───────────────────────────────────────────────────────────

/** GET /api/v1/workflow/definitions/{key}/versions — list all versions for a definition key */
export function useVersionHistory(defKey: string) {
  return useQuery({
    queryKey: migrationKeys.versionHistory(defKey),
    queryFn: () =>
      apiClient
        .get<VersionHistoryDto[]>(`/workflow/definitions/${defKey}/versions`)
        .then((r) => r.data),
    enabled: !!defKey,
  });
}

// ─── Migration Queries ─────────────────────────────────────────────────────────

/** GET /api/v1/workflow/definitions/{key}/migrations — list migrations for a definition */
export function useMigrationsByDef(defKey: string) {
  return useQuery({
    queryKey: migrationKeys.migrationsByDef(defKey),
    queryFn: () =>
      apiClient
        .get<MigrationDto[]>(`/workflow/definitions/${defKey}/migrations`)
        .then((r) => r.data),
    enabled: !!defKey,
  });
}

/** GET /api/v1/workflow/migrations/{id} — single migration detail.
 *  Polls every 3s while InProgress to handle case where SignalR hub is not wired. */
export function useMigration(id: string) {
  return useQuery({
    queryKey: migrationKeys.migration(id),
    queryFn: () =>
      apiClient.get<MigrationDto>(`/workflow/migrations/${id}`).then((r) => r.data),
    enabled: !!id,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'InProgress' ? 3000 : false;
    },
  });
}

/** GET /api/v1/workflow/migrations/{id}/items — paged migration items with optional status filter */
export function useMigrationItems(
  id: string,
  page: number,
  status?: MigrationItemStatus,
) {
  return useQuery({
    queryKey: migrationKeys.migrationItems(id, page, status),
    queryFn: () =>
      apiClient
        .get<PagedResult<MigrationItemDto>>(`/workflow/migrations/${id}/items`, {
          params: { page, pageSize: 50, ...(status ? { status } : {}) },
        })
        .then((r) => r.data),
    enabled: !!id,
  });
}

// ─── Mutations ─────────────────────────────────────────────────────────────────

/** POST /api/v1/workflow/migrations/validate — dry-run mapping validation */
export function useValidateMapping() {
  return useMutation({
    mutationFn: (body: CreateMigrationRequest) =>
      apiClient
        .post<MigrationValidationResult>('/workflow/migrations/validate', body)
        .then((r) => r.data),
  });
}

/** POST /api/v1/workflow/migrations — create migration (status will be PendingApproval) */
export function useCreateMigration(defKey: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateMigrationRequest) =>
      apiClient.post<MigrationDto>('/workflow/migrations', body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: migrationKeys.migrationsByDef(defKey) });
    },
  });
}

/** POST /api/v1/workflow/migrations/{id}/approve */
export function useApproveMigration(defKey: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post<MigrationDto>(`/workflow/migrations/${id}/approve`).then((r) => r.data),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: migrationKeys.migration(data.id) });
      qc.invalidateQueries({ queryKey: migrationKeys.migrationsByDef(defKey) });
    },
  });
}

/** POST /api/v1/workflow/migrations/{id}/execute */
export function useExecuteMigration(defKey: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post<MigrationDto>(`/workflow/migrations/${id}/execute`).then((r) => r.data),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: migrationKeys.migration(data.id) });
      qc.invalidateQueries({ queryKey: migrationKeys.migrationsByDef(defKey) });
    },
  });
}

/** POST /api/v1/workflow/migrations/{id}/cancel */
export function useCancelMigration(defKey: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post<MigrationDto>(`/workflow/migrations/${id}/cancel`).then((r) => r.data),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: migrationKeys.migration(data.id) });
      qc.invalidateQueries({ queryKey: migrationKeys.migrationsByDef(defKey) });
    },
  });
}
