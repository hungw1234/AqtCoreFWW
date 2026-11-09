// Analytics API hooks — 6 GET endpoints under /api/v1/workflow/analytics
// Client-side cache: 5 min staleTime (BE already caches 15 min — FE layer ensures instant UX)

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  StateDistributionDto,
  StateProcessingTimeDto,
  UserThroughputDto,
  SlaComplianceTrendDto,
  AgingInstanceDto,
  BottleneckDto,
  GroupByPeriod,
} from './workflow-types';

const STALE_5_MIN = 5 * 60 * 1000;

// ─── Query Keys ────────────────────────────────────────────────────────────────

export const analyticsKeys = {
  stateDistribution: (definitionId?: string) =>
    ['workflow', 'analytics', 'state-distribution', definitionId] as const,
  processingTime: (definitionId?: string, from?: string, to?: string) =>
    ['workflow', 'analytics', 'processing-time', definitionId, from, to] as const,
  throughput: (definitionId?: string, from?: string, to?: string, groupBy?: GroupByPeriod) =>
    ['workflow', 'analytics', 'throughput', definitionId, from, to, groupBy] as const,
  slaCompliance: (definitionId?: string, from?: string, to?: string, groupBy?: GroupByPeriod) =>
    ['workflow', 'analytics', 'sla-compliance', definitionId, from, to, groupBy] as const,
  aging: (minHours?: number) =>
    ['workflow', 'analytics', 'aging', minHours] as const,
  bottlenecks: (definitionId?: string, from?: string, to?: string) =>
    ['workflow', 'analytics', 'bottlenecks', definitionId, from, to] as const,
};

// ─── Hooks ─────────────────────────────────────────────────────────────────────

/** GET /api/v1/workflow/analytics/state-distribution?definitionId= */
export function useStateDistribution(definitionId?: string) {
  return useQuery({
    queryKey: analyticsKeys.stateDistribution(definitionId),
    queryFn: () =>
      apiClient
        .get<StateDistributionDto[]>('/workflow/analytics/state-distribution', {
          params: { definitionId: definitionId || undefined },
        })
        .then((r) => r.data),
    staleTime: STALE_5_MIN,
  });
}

/** GET /api/v1/workflow/analytics/processing-time?definitionId=&from=&to= */
export function useProcessingTime(definitionId?: string, from?: string, to?: string) {
  return useQuery({
    queryKey: analyticsKeys.processingTime(definitionId, from, to),
    queryFn: () =>
      apiClient
        .get<StateProcessingTimeDto[]>('/workflow/analytics/processing-time', {
          params: { definitionId: definitionId || undefined, from, to },
        })
        .then((r) => r.data),
    staleTime: STALE_5_MIN,
  });
}

/** GET /api/v1/workflow/analytics/throughput?definitionId=&from=&to=&groupBy= */
export function useThroughput(
  definitionId?: string,
  from?: string,
  to?: string,
  groupBy: GroupByPeriod = 'Day',
) {
  return useQuery({
    queryKey: analyticsKeys.throughput(definitionId, from, to, groupBy),
    queryFn: () =>
      apiClient
        .get<UserThroughputDto[]>('/workflow/analytics/throughput', {
          params: { definitionId: definitionId || undefined, from, to, groupBy },
        })
        .then((r) => r.data),
    staleTime: STALE_5_MIN,
  });
}

/** GET /api/v1/workflow/analytics/sla-compliance?definitionId=&from=&to=&groupBy= */
export function useSlaCompliance(
  definitionId?: string,
  from?: string,
  to?: string,
  groupBy: GroupByPeriod = 'Day',
) {
  return useQuery({
    queryKey: analyticsKeys.slaCompliance(definitionId, from, to, groupBy),
    queryFn: () =>
      apiClient
        .get<SlaComplianceTrendDto[]>('/workflow/analytics/sla-compliance', {
          params: { definitionId: definitionId || undefined, from, to, groupBy },
        })
        .then((r) => r.data),
    staleTime: STALE_5_MIN,
  });
}

/** GET /api/v1/workflow/analytics/aging?minHours= */
export function useAgingReport(minHours = 24) {
  return useQuery({
    queryKey: analyticsKeys.aging(minHours),
    queryFn: () =>
      apiClient
        .get<AgingInstanceDto[]>('/workflow/analytics/aging', {
          params: { minHours },
        })
        .then((r) => r.data),
    staleTime: STALE_5_MIN,
  });
}

/** GET /api/v1/workflow/analytics/bottlenecks?definitionId=&from=&to= */
export function useBottlenecks(definitionId?: string, from?: string, to?: string) {
  return useQuery({
    queryKey: analyticsKeys.bottlenecks(definitionId, from, to),
    queryFn: () =>
      apiClient
        .get<BottleneckDto[]>('/workflow/analytics/bottlenecks', {
          params: { definitionId: definitionId || undefined, from, to },
        })
        .then((r) => r.data),
    staleTime: STALE_5_MIN,
  });
}
