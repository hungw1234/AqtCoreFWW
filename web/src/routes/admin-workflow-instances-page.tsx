// Route: /admin/workflow/instances — wires cursor-paginated hooks to monitoring dashboard.
// RT-04: file lives in flat routes dir (not features/workflow/).
// RT-05: uses hand-written apiClient.get hooks — no orval-generated client.

import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { InstanceMonitoringDashboard } from '@/features/workflow/instance-monitoring-dashboard';
import {
  useWorkflowInstancesInfinite,
  useWorkflowInstancesSummary,
} from '@/features/workflow/use-workflow-instances-infinite';
import { useState } from 'react';
import type { InstanceFilter } from '@/features/workflow/use-workflow-instances-infinite';

// ─── Page component ────────────────────────────────────────────────────────────

/**
 * Wrapper page component — manages filter state and wires both hooks to the dashboard.
 * Separated from the dashboard so the dashboard remains a pure presentational component.
 */
function WorkflowInstancesPage() {
  // Default: show active instances on load
  const [filter, setFilter] = useState<InstanceFilter>({ status: 'active' });

  const {
    data,
    isLoading,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useWorkflowInstancesInfinite(filter);

  const { data: summary } = useWorkflowInstancesSummary(filter);

  // Flatten all loaded pages into a single array for the virtualizer
  const flatInstances = data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <InstanceMonitoringDashboard
      instances={flatInstances}
      summary={summary}
      isLoading={isLoading}
      hasNextPage={hasNextPage}
      isFetchingNextPage={isFetchingNextPage}
      onLoadMore={() => fetchNextPage()}
      filter={filter}
      onFilterChange={setFilter}
    />
  );
}

// ─── Route definition ──────────────────────────────────────────────────────────

export const adminWorkflowInstancesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow/instances',
  component: WorkflowInstancesPage,
});
