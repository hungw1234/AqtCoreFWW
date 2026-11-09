// inbox-api.ts — typed hook wrapper for workflow inbox endpoint
// NOTE: Orval generates apiClientMutator<void> (no response type).
// Response interceptor unwraps the envelope {success,data,meta} → inner data; mutator returns it as-is.
// Inbox endpoint returns bare WorkflowTaskDto[] (not PagedResult) → TData = WorkflowTaskDto[].

import { useGetApiV1WorkflowTasksInbox } from '@/api/generated/workflow-tasks/workflow-tasks';

// ─── Response DTO ────────────────────────────────────────────────────────────

/** Matches WorkflowTaskDto from BE (camelCase flat, no nested dicts) */
export interface WorkflowTaskDto {
  id: number;
  workflowInstanceId: string; // Guid — nav target for /business-process-runs/$id
  title: string;
  description: string | null;
  assigneeId: string | null; // null = unassigned pool task
  status: 'Open' | 'InProgress' | string;
  priority: 'Normal' | string;
  dueDate: string | null;
  completedAt: string | null;
  completedBy: string | null;
  tenantId: string;
  createdAt: string;
  createdBy: string;
  delegatedFromUserId: string | null;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const inboxQueryKeys = {
  all: ['inbox'] as const,
  list: () => ['inbox', 'list'] as const,
};

// ─── Hook ────────────────────────────────────────────────────────────────────

/**
 * Fetch workflow inbox — tasks assigned to or claimable by the current user.
 * Endpoint returns a bounded full set per user (~1–100 tasks) with no pagination cursor.
 * TData = bare WorkflowTaskDto[] (envelope already unwrapped by apiClientMutator).
 */
export function useMyInbox() {
  return useGetApiV1WorkflowTasksInbox<WorkflowTaskDto[]>({
    query: { refetchInterval: 60_000 },
  });
}
