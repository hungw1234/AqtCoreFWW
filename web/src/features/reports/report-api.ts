import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  ReportDefinitionDto,
  ReportExecutionDto,
  CreateReportDefinitionRequest,
  RunReportRequest,
  ExecutionStatus,
} from './report-types';

// ─── Query keys ───────────────────────────────────────────────────────────────

export const reportQueryKeys = {
  definitions: ['reports', 'definitions'] as const,
  execution: (id: string) => ['reports', 'execution', id] as const,
};

// ─── Queries ──────────────────────────────────────────────────────────────────

/** GET /api/v1/reports/definitions */
export function useReportDefinitions() {
  return useQuery({
    queryKey: reportQueryKeys.definitions,
    queryFn: () =>
      apiClient.get<ReportDefinitionDto[]>('/reports/definitions').then((r) => r.data),
  });
}

/** GET /api/v1/reports/executions/{id} — polls while status is Queued or Running */
export function useReportExecution(id: string | null) {
  return useQuery({
    queryKey: reportQueryKeys.execution(id ?? ''),
    queryFn: () =>
      apiClient.get<ReportExecutionDto>(`/reports/executions/${id}`).then((r) => r.data),
    enabled: Boolean(id),
    // Stop polling once terminal state is reached
    refetchInterval: (query) => {
      const status = query.state.data?.status as ExecutionStatus | undefined;
      if (status === 'Done' || status === 'Failed') return false;
      return 3_000;
    },
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/reports/definitions (Admin only) */
export function useCreateReportDefinition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateReportDefinitionRequest) =>
      apiClient.post<ReportDefinitionDto>('/reports/definitions', body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: reportQueryKeys.definitions });
    },
  });
}

/** POST /api/v1/reports/run — returns 202 with executionId */
export function useRunReport() {
  return useMutation({
    mutationFn: (body: RunReportRequest) =>
      apiClient.post<string>('/reports/run', body).then((r) => r.data),
  });
}

/** Trigger authenticated browser download for a completed execution.
 *  Uses apiClient (Bearer token) instead of window.open (no auth header).
 */
export async function downloadReport(executionId: string): Promise<void> {
  const response = await apiClient.get(
    `/reports/executions/${executionId}/download`,
    { responseType: 'blob' },
  );

  // Extract filename from Content-Disposition header when available
  const disposition = (response.headers['content-disposition'] as string) ?? '';
  const filenameMatch = disposition.match(/filename="?([^";\n]+)"?/);
  const filename = filenameMatch?.[1] ?? `report-${executionId}.xlsx`;

  const blobUrl = URL.createObjectURL(response.data as Blob);
  const anchor = document.createElement('a');
  anchor.href = blobUrl;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(blobUrl);
}
