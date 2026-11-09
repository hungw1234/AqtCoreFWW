// Workflow API hooks — React Query wrappers for all workflow endpoints

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  WorkflowDefinitionDto,
  WorkflowDefinitionListDto,
  CreateWorkflowRequest,
  SaveGraphRequest,
  WorkflowNotificationConfigDto,
  CreateNotificationConfigRequest,
  WorkflowAssignmentRuleDto,
  CreateAssignmentRuleRequest,
  WorkflowTaskDto,
  WorkflowInstanceTaskDto,
  WorkflowInstanceDto,
  WorkflowTransitionDto,
  ExecuteTransitionRequest,
  ReassignTaskRequest,
  BatchTransitionRequest,
  BatchTransitionResult,
  DelegationRuleDto,
  DelegateTaskRequest,
  CreateDelegationRuleRequest,
  UpdateDelegationRuleRequest,
} from './workflow-types';

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ─── Query Keys ────────────────────────────────────────────────────────────────

export const workflowKeys = {
  all: ['workflow'] as const,
  definitions: (page: number, pageSize: number, search?: string) => ['workflow', 'definitions', page, pageSize, search] as const,
  definition: (id: string) => ['workflow', 'definition', id] as const,
  notificationConfigs: (defId: string) => ['workflow', 'notif-configs', defId] as const,
  tasksByAssignee: (assigneeId: string) => ['workflow', 'tasks', 'assignee', assigneeId] as const,
  tasksByInstance: (instanceId: string) => ['workflow', 'tasks', 'instance', instanceId] as const,
  instance: (id: string) => ['workflow', 'instance', id] as const,
  availableTransitions: (instanceId: string) => ['workflow', 'transitions', instanceId] as const,
  assignmentRules: () => ['workflow', 'assignment-rules'] as const,
  delegationRules: () => ['workflow', 'delegation-rules'] as const,
};

// ─── Definitions ───────────────────────────────────────────────────────────────

/** GET /api/v1/workflow/definitions — paged list with optional search */
export function useWorkflowDefinitions(page = 1, pageSize = 20, search?: string) {
  return useQuery({
    queryKey: workflowKeys.definitions(page, pageSize, search),
    queryFn: () =>
      apiClient
        .get<PagedResult<WorkflowDefinitionListDto>>('/workflow/definitions', {
          params: { page, pageSize, search: search || undefined },
        })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

/** GET /api/v1/workflow/definitions/:id — single definition with states+transitions */
export function useWorkflowDefinition(id: string) {
  return useQuery({
    queryKey: workflowKeys.definition(id),
    queryFn: () =>
      apiClient
        .get<WorkflowDefinitionDto>(`/workflow/definitions/${id}`)
        .then((r) => normalizeDefinitionIds(r.data)),
    enabled: !!id,
  });
}

// BE returns long bigint ids for T1 WorkflowState/WorkflowTransition entities
// (arch:accept-long — "FE round-trips opaque"). FE TS types + ReactFlow Node.id
// strictly require string. Coerce at API boundary so all downstream components
// (canvas, find() lookups, react-flow nodes/edges) work uniformly.
export function normalizeDefinitionIds(d: WorkflowDefinitionDto): WorkflowDefinitionDto {
  return {
    ...d,
    states: d.states.map((s) => ({
      ...s,
      id: String(s.id),
      autoTransitionOnTimeoutId:
        s.autoTransitionOnTimeoutId != null ? String(s.autoTransitionOnTimeoutId) : null,
    })),
    transitions: d.transitions.map((t) => ({
      ...t,
      id: String(t.id),
      fromStateId: String(t.fromStateId),
      toStateId: String(t.toStateId),
    })),
    // Branch state-refs must be coerced too: the save path resolves them to names via a
    // string-keyed state map — a numeric ref misses the map and leaks a raw id into the
    // name-keyed save payload, which the BE rejects (400).
    branches: (d.branches ?? []).map((b) => ({
      ...b,
      id: String(b.id),
      branchingStateId: String(b.branchingStateId),
      continuationStateId: String(b.continuationStateId),
      children: b.children.map((c) => ({
        ...c,
        id: String(c.id),
        targetStateId: String(c.targetStateId),
        resolutions: c.resolutions.map((r) => ({
          ...r,
          resultingStateId: r.resultingStateId != null ? String(r.resultingStateId) : null,
        })),
      })),
    })),
  };
}

/** POST /api/v1/workflow/definitions — create new definition */
export function useCreateWorkflow() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateWorkflowRequest) =>
      apiClient
        .post<WorkflowDefinitionDto>('/workflow/definitions', body)
        .then((r) => normalizeDefinitionIds(r.data)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] }),
  });
}

/** PUT /api/v1/workflow/definitions/:id — update metadata (name, description) */
export function useUpdateWorkflowDefinition(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; description: string }) =>
      apiClient
        .put<WorkflowDefinitionDto>(`/workflow/definitions/${id}`, body)
        .then((r) => normalizeDefinitionIds(r.data)),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowKeys.definition(id) });
      qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] });
    },
  });
}

/** DELETE /api/v1/workflow/definitions/:id — soft delete */
export function useDeleteWorkflowDefinition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/workflow/definitions/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] }),
  });
}

/** POST /api/v1/workflow/definitions/:id/clone — clone as new version */
export function useCloneWorkflowDefinition(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiClient
        .post<WorkflowDefinitionDto>(`/workflow/definitions/${id}/clone`)
        .then((r) => normalizeDefinitionIds(r.data)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] }),
  });
}

/** PUT /api/v1/workflow/definitions/:id/graph — save all states+transitions */
export function useSaveDefinitionGraph(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveGraphRequest) =>
      apiClient
        .put<WorkflowDefinitionDto>(`/workflow/definitions/${id}/graph`, body)
        .then((r) => normalizeDefinitionIds(r.data)),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowKeys.definition(id) });
      qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] });
    },
  });
}

/** POST /api/v1/workflow/definitions/:id/activate */
export function useActivateDefinition(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiClient.post(`/workflow/definitions/${id}/activate`).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowKeys.definition(id) });
      qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] });
    },
  });
}

/** POST /api/v1/workflow/definitions/:id/deactivate */
export function useDeactivateDefinition(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiClient.post(`/workflow/definitions/${id}/deactivate`).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowKeys.definition(id) });
      qc.invalidateQueries({ queryKey: ['workflow', 'definitions'] });
    },
  });
}

// ─── Notification Configs ──────────────────────────────────────────────────────

/** GET /api/v1/workflow/definitions/:id/notification-configs */
export function useNotificationConfigs(defId: string) {
  return useQuery({
    queryKey: workflowKeys.notificationConfigs(defId),
    queryFn: () =>
      apiClient
        .get<WorkflowNotificationConfigDto[]>(`/workflow/definitions/${defId}/notification-configs`)
        .then((r) => r.data),
    enabled: !!defId,
  });
}

/** POST /api/v1/workflow/definitions/:id/notification-configs */
export function useCreateNotificationConfig(defId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateNotificationConfigRequest) =>
      apiClient
        .post<WorkflowNotificationConfigDto>(
          `/workflow/definitions/${defId}/notification-configs`,
          body,
        )
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.notificationConfigs(defId) }),
  });
}

/** DELETE /api/v1/workflow/definitions/:defId/notification-configs/:configId */
export function useDeleteNotificationConfig(defId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (configId: string) =>
      apiClient
        .delete(`/workflow/definitions/${defId}/notification-configs/${configId}`)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.notificationConfigs(defId) }),
  });
}

// ─── Assignment Rules ──────────────────────────────────────────────────────────

/** GET /api/v1/workflow/assignments */
export function useAssignmentRules() {
  return useQuery({
    queryKey: workflowKeys.assignmentRules(),
    queryFn: () =>
      apiClient
        .get<WorkflowAssignmentRuleDto[]>('/workflow/assignments')
        .then((r) => r.data),
  });
}

/** POST /api/v1/workflow/assignments */
export function useCreateAssignmentRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAssignmentRuleRequest) =>
      apiClient
        .post<WorkflowAssignmentRuleDto>('/workflow/assignments', body)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.assignmentRules() }),
  });
}

/** DELETE /api/v1/workflow/assignments/:id */
export function useDeleteAssignmentRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/workflow/assignments/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.assignmentRules() }),
  });
}

// ─── Tasks ─────────────────────────────────────────────────────────────────────

/** GET /api/v1/workflow/tasks/by-assignee/:assigneeId */
export function useTasksByAssignee(assigneeId: string) {
  return useQuery({
    queryKey: workflowKeys.tasksByAssignee(assigneeId),
    queryFn: () =>
      apiClient.get<WorkflowTaskDto[]>(`/workflow/tasks/by-assignee/${assigneeId}`).then((r) => r.data),
    enabled: !!assigneeId,
    refetchInterval: 30_000, // poll every 30s
    staleTime: 30_000, // within the poll window a remount serves cache; scheduled poll still refetches
  });
}

/** GET /api/v1/workflow/tasks/by-instance/:instanceId — tasks enriched with assignee name + candidate pool */
export function useTasksByInstance(instanceId: string) {
  return useQuery({
    queryKey: workflowKeys.tasksByInstance(instanceId),
    queryFn: () =>
      apiClient
        .get<WorkflowInstanceTaskDto[]>(`/workflow/tasks/by-instance/${instanceId}`)
        .then((r) => r.data),
    enabled: !!instanceId,
  });
}

/** POST /api/v1/workflow/tasks/:id/claim — eligible pool member takes ownership of a pool task.
 * `suppressAuthInterstitial` opts out of the global 403 toast/redirect so a caller that treats
 * 403 as a routine "not pool-eligible" outcome (e.g. optimistic-show Claim) can render its own. */
export function useClaimTask(options?: { suppressAuthInterstitial?: boolean }) {
  return useMutation({
    mutationFn: (taskId: string | number) =>
      apiClient
        .post(
          `/workflow/tasks/${taskId}/claim`,
          undefined,
          options?.suppressAuthInterstitial ? { suppressAuthInterstitial: true } : undefined,
        )
        .then((r) => r.data),
  });
}

/** POST /api/v1/workflow/tasks/:id/unclaim — assignee releases a claimed pool task back to the pool */
export function useUnclaimTask() {
  return useMutation({
    mutationFn: (taskId: string | number) =>
      apiClient.post(`/workflow/tasks/${taskId}/unclaim`).then((r) => r.data),
  });
}

/** POST /api/v1/workflow/tasks/:id/reassign */
export function useReassignTask() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, body }: { taskId: string; body: ReassignTaskRequest }) =>
      apiClient.post(`/workflow/tasks/${taskId}/reassign`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'tasks'] }),
  });
}

/** POST /api/v1/workflow/tasks/:id/complete */
export function useCompleteTask() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (taskId: string) =>
      apiClient.post(`/workflow/tasks/${taskId}/complete`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'tasks'] }),
  });
}

// ─── Instances ─────────────────────────────────────────────────────────────────

/** GET /api/v1/workflow/instances/:id */
export function useWorkflowInstance(instanceId: string) {
  return useQuery({
    queryKey: workflowKeys.instance(instanceId),
    queryFn: () =>
      apiClient.get<WorkflowInstanceDto>(`/workflow/instances/${instanceId}`).then((r) => r.data),
    enabled: !!instanceId,
  });
}

/** GET /api/v1/workflow/instances/:id/available-transitions */
export function useAvailableTransitions(instanceId: string) {
  return useQuery({
    queryKey: workflowKeys.availableTransitions(instanceId),
    queryFn: () =>
      apiClient
        .get<WorkflowTransitionDto[]>(`/workflow/instances/${instanceId}/available-transitions`)
        .then((r) => r.data),
    enabled: !!instanceId,
  });
}

/** POST /api/v1/workflow/instances/:id/transitions */
export function useExecuteTransition(instanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ExecuteTransitionRequest) =>
      apiClient.post(`/workflow/instances/${instanceId}/transitions`, body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowKeys.instance(instanceId) });
      qc.invalidateQueries({ queryKey: workflowKeys.availableTransitions(instanceId) });
      qc.invalidateQueries({ queryKey: ['workflow', 'tasks'] });
    },
  });
}

/**
 * POST /api/v1/workflow/instances/batch-transition — execute same transition on multiple instances.
 * Max 50 items per batch. Partial-failure semantics — each item reports independent status.
 */
export function useBatchTransition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: BatchTransitionRequest) =>
      // api-client interceptor already unwraps ApiResponse envelope → r.data is payload
      apiClient
        .post<BatchTransitionResult>('/workflow/instances/batch-transition', body)
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowKeys.all });
      qc.invalidateQueries({ queryKey: ['workflow', 'tasks'] });
    },
  });
}

// ─── Delegation ────────────────────────────────────────────────────────────────

/** GET /api/v1/workflow/delegation-rules — list rules for current tenant */
export function useDelegationRules() {
  return useQuery({
    queryKey: workflowKeys.delegationRules(),
    queryFn: () =>
      apiClient.get<DelegationRuleDto[]>('/workflow/delegation-rules').then((r) => r.data),
  });
}

/** POST /api/v1/workflow/delegation-rules */
export function useCreateDelegationRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDelegationRuleRequest) =>
      apiClient.post<DelegationRuleDto>('/workflow/delegation-rules', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.delegationRules() }),
  });
}

/** PUT /api/v1/workflow/delegation-rules/{id} */
export function useUpdateDelegationRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateDelegationRuleRequest }) =>
      apiClient.put<DelegationRuleDto>(`/workflow/delegation-rules/${id}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.delegationRules() }),
  });
}

/** DELETE /api/v1/workflow/delegation-rules/{id} — soft delete */
export function useDeleteDelegationRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/workflow/delegation-rules/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowKeys.delegationRules() }),
  });
}

/** POST /api/v1/workflow/tasks/{id}/delegate */
export function useDelegateTask() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, body }: { taskId: string; body: DelegateTaskRequest }) =>
      apiClient.post<WorkflowTaskDto>(`/workflow/tasks/${taskId}/delegate`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'tasks'] }),
  });
}

/** POST /api/v1/workflow/tasks/{id}/return-delegation — no body */
export function useReturnDelegation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (taskId: string) =>
      apiClient.post<WorkflowTaskDto>(`/workflow/tasks/${taskId}/return-delegation`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['workflow', 'tasks'] }),
  });
}

// ─── Parallel Branch Types ─────────────────────────────────────────────────────

export interface BranchChildStatusDto {
  id: string;
  branchChildId: string;
  /** 0=Approved, 1=Rejected, 2=Escalated, 3=Timeout — null means pending */
  resolutionType?: number;
  resolvedBy?: string;
  resolvedAt?: string;
  comment?: string;
  /** Task assignee — used for F3 button visibility (only assignee sees resolve buttons) */
  assigneeId?: string;
  /** Live (non-cancelled) WorkflowTask id for this child — drives pool Claim. Null until fork stages it. */
  taskId?: number;
}

export interface BranchStatusDto {
  id: string;
  instanceId: string;
  parallelBranchId: string;
  status: string;
  resolvedAt?: string;
  childStatuses: BranchChildStatusDto[];
}

export interface ResolveBranchChildRequest {
  resolutionType: number;
  comment?: string;
}

/** Numeric resolution type constants */
export const BranchResolution = {
  Approved: 0,
  Rejected: 1,
  Escalated: 2,
  Timeout: 3,
} as const;

// ─── Parallel Branch Hooks ─────────────────────────────────────────────────────

/**
 * GET /api/v1/workflow/instances/:id/branch-status
 * Polls every 10 s to reflect live approver activity.
 */
export function useBranchStatuses(instanceId: string) {
  return useQuery({
    queryKey: ['workflow-branch-status', instanceId],
    queryFn: () =>
      apiClient
        .get<BranchStatusDto[]>(`/workflow/instances/${instanceId}/branch-status`)
        .then((r) => r.data),
    enabled: !!instanceId,
    refetchInterval: 10_000,
    staleTime: 10_000, // within the poll window a remount serves cache; scheduled poll still refetches
  });
}

/**
 * POST /api/v1/workflow/instances/:id/branch-children/:childStatusId/resolve
 * Invalidates both branch-status and instance queries on success.
 */
export function useResolveBranchChild(instanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      childStatusId,
      ...body
    }: { childStatusId: string } & ResolveBranchChildRequest) =>
      apiClient
        .post(
          `/workflow/instances/${instanceId}/branch-children/${childStatusId}/resolve`,
          body,
        )
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['workflow-branch-status', instanceId] });
      qc.invalidateQueries({ queryKey: workflowKeys.instance(instanceId) });
    },
  });
}
