// Jobs API hooks — wraps /api/v1/admin/jobs (Hangfire monitoring)

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types (match BE JobSummaryDto) ──────────────────────────────────────────

export interface JobSummaryDto {
  id: string;
  name: string;
  status: string;
  createdAt?: string;
  lastExecution?: string;
  nextExecution?: string;
  cron?: string;
}

export interface JobsResponse {
  items: JobSummaryDto[];
  totalCount: number;
  counts: Record<string, number>;
}

export interface JobStatsDto {
  enqueued: number;
  processing: number;
  scheduled: number;
  succeeded: number;
  failed: number;
  pending: number;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const jobsQueryKeys = {
  all: ['jobs'] as const,
  list: (status?: string) => ['jobs', 'list', status] as const,
  stats: ['jobs', 'stats'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/jobs — list jobs (recurring + failed by default) */
export function useJobs(status?: string) {
  return useQuery({
    queryKey: jobsQueryKeys.list(status),
    queryFn: () =>
      apiClient
        .get<JobsResponse>('/admin/jobs', { params: status ? { status } : undefined })
        .then((r) => r.data),
  });
}

/** GET /api/v1/admin/jobs?status=Failed — failed jobs only */
export function useFailedJobs() {
  return useJobs('Failed');
}

/** GET /api/v1/admin/jobs/stats — aggregated statistics */
export function useJobStats() {
  return useQuery({
    queryKey: jobsQueryKeys.stats,
    queryFn: () =>
      apiClient.get<JobStatsDto>('/admin/jobs/stats').then((r) => r.data),
  });
}
