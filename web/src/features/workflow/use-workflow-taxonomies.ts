// Phase-00 Chunk 4a — TanStack Query hooks + types for workflow taxonomy master data.
// Backend: WorkflowTaxonomyAdminController (GET /admin/master-data/workflow-taxonomy/{states,actions}).
// Read policy: MASTER.WORKFLOW_TAXONOMY.VIEW — any authenticated admin.
//
// apiClient unwraps ApiResponse<T> envelope via interceptor, so queryFn returns the `data` array directly.
// Cache: staleTime 5min on FE. BE Redis keys (mst:workflow-*-taxonomy:all) invalidate instantly on write;
// 5min FE window only covers the latency between BE commit and next admin page visit.

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types — mirror BE DTOs (WorkflowStateTaxonomyDto, WorkflowActionTaxonomyDto) ──

export type WorkflowStateCategory = 'Initial' | 'Intermediate' | 'Terminal' | 'Rejected';

export interface WorkflowStateTaxonomyDto {
  id: string;
  code: string;
  nameVi: string;
  nameEn: string;
  i18nKey?: string | null;
  icon?: string | null;
  color?: string | null;
  category: WorkflowStateCategory | string; // BE serializes enum as string
  sortOrder: number;
  isActive: boolean;
  deprecatedReason?: string | null;
  rowVersion: string; // base64-encoded byte[]
}

export interface WorkflowActionTaxonomyDto {
  id: string;
  code: string;
  nameVi: string;
  nameEn: string;
  i18nKey?: string | null;
  icon?: string | null;
  sortOrder: number;
  isActive: boolean;
  deprecatedReason?: string | null;
  rowVersion: string;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const workflowTaxonomyKeys = {
  states: ['workflow-taxonomy', 'states'] as const,
  actions: ['workflow-taxonomy', 'actions'] as const,
};

// ─── Hooks ───────────────────────────────────────────────────────────────────

const ROUTE = '/admin/master-data/workflow-taxonomy';
const STALE_MS = 5 * 60 * 1000;

/** List active workflow state taxonomy rows (default page=1, pageSize=200). */
export function useWorkflowStateTaxonomies() {
  return useQuery({
    queryKey: workflowTaxonomyKeys.states,
    queryFn: () =>
      apiClient
        .get<WorkflowStateTaxonomyDto[]>(`${ROUTE}/states`)
        .then((r) => r.data ?? []),
    staleTime: STALE_MS,
  });
}

/** List active workflow action taxonomy rows (default page=1, pageSize=200). */
export function useWorkflowActionTaxonomies() {
  return useQuery({
    queryKey: workflowTaxonomyKeys.actions,
    queryFn: () =>
      apiClient
        .get<WorkflowActionTaxonomyDto[]>(`${ROUTE}/actions`)
        .then((r) => r.data ?? []),
    staleTime: STALE_MS,
  });
}

// ─── Helpers for Select autocomplete ─────────────────────────────────────────

export interface TaxonomyOption {
  label: string; // display — `Code — NameVi` format
  value: string; // the Code (copied into WorkflowDefinitionState.Name / Transition.ActionName)
}

/** Build antd Select options from taxonomy DTOs — code as value, "Code — NameVi" as label. */
export function toTaxonomyOptions(
  rows: ReadonlyArray<{ code: string; nameVi: string; nameEn: string }>
): TaxonomyOption[] {
  return rows.map((r) => ({
    label: `${r.code} — ${r.nameVi}`,
    value: r.code,
  }));
}

/**
 * Returns true when `code` does NOT match any dictionary row (case-insensitive).
 * Used by Select consumers to render a yellow warning — custom codes are allowed but flagged.
 */
export function isCustomCode(
  code: string | undefined | null,
  rows: ReadonlyArray<{ code: string }>
): boolean {
  if (!code) return false;
  const needle = code.toLowerCase();
  return !rows.some((r) => r.code.toLowerCase() === needle);
}

// ─── Mutation payloads (C4b-1 — state taxonomy admin CRUD) ───────────────────

export interface CreateWorkflowStateTaxonomyPayload {
  code: string;
  nameVi: string;
  nameEn: string;
  category: WorkflowStateCategory | string;
  sortOrder: number;
  i18nKey?: string | null;
  icon?: string | null;
  color?: string | null;
}

export interface UpdateWorkflowStateTaxonomyPayload {
  id: string; // URL segment — stripped from POST body
  nameVi: string;
  nameEn: string;
  category: WorkflowStateCategory | string;
  sortOrder: number;
  rowVersion: string;
  i18nKey?: string | null;
  icon?: string | null;
  color?: string | null;
}

// ─── Mutation hooks ──────────────────────────────────────────────────────────

/** POST /admin/master-data/workflow-taxonomy/states — creates a new state taxonomy row. */
export function useCreateWorkflowStateTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateWorkflowStateTaxonomyPayload) =>
      apiClient
        .post<{ id: string }>(`${ROUTE}/states`, payload)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.states }),
  });
}

/** PUT /admin/master-data/workflow-taxonomy/states/{id} — update with RowVersion concurrency. */
export function useUpdateWorkflowStateTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: UpdateWorkflowStateTaxonomyPayload) =>
      apiClient
        .put<void>(`${ROUTE}/states/${id}`, body)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.states }),
  });
}

// ─── Mutation payloads (C4b-2 — action taxonomy admin CRUD) ──────────────────

// Actions DTO has no Category and no Color (plan §Entity — WorkflowActionTaxonomy spec).

export interface CreateWorkflowActionTaxonomyPayload {
  code: string;
  nameVi: string;
  nameEn: string;
  sortOrder: number;
  i18nKey?: string | null;
  icon?: string | null;
}

export interface UpdateWorkflowActionTaxonomyPayload {
  id: string; // URL segment — stripped from POST body
  nameVi: string;
  nameEn: string;
  sortOrder: number;
  rowVersion: string;
  i18nKey?: string | null;
  icon?: string | null;
}

/** POST /admin/master-data/workflow-taxonomy/actions — creates a new action taxonomy row. */
export function useCreateWorkflowActionTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateWorkflowActionTaxonomyPayload) =>
      apiClient
        .post<{ id: string }>(`${ROUTE}/actions`, payload)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.actions }),
  });
}

/** PUT /admin/master-data/workflow-taxonomy/actions/{id} — update with RowVersion concurrency. */
export function useUpdateWorkflowActionTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: UpdateWorkflowActionTaxonomyPayload) =>
      apiClient
        .put<void>(`${ROUTE}/actions/${id}`, body)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.actions }),
  });
}

// ─── C4c — Archive tab query (includeInactive=true) + Delete/Restore mutations ──

export const workflowTaxonomyArchivedKeys = {
  states: ['workflow-taxonomy', 'states', 'archived'] as const,
  actions: ['workflow-taxonomy', 'actions', 'archived'] as const,
};

export interface DeleteTaxonomyPayload {
  id: string;
  confirmCode: string; // case-SENSITIVE match against Code
  reason: string;      // min 10 chars per BE validator
}

/** GET .../states?includeInactive=true — admin archive view (deprecated rows included). */
export function useArchivedWorkflowStateTaxonomies() {
  return useQuery({
    queryKey: workflowTaxonomyArchivedKeys.states,
    queryFn: () =>
      apiClient
        .get<WorkflowStateTaxonomyDto[]>(`${ROUTE}/states`, {
          params: { includeInactive: true },
        })
        .then((r) => (r.data ?? []).filter((row) => !row.isActive)),
    staleTime: STALE_MS,
  });
}

/** GET .../actions?includeInactive=true — admin archive view (deprecated rows included). */
export function useArchivedWorkflowActionTaxonomies() {
  return useQuery({
    queryKey: workflowTaxonomyArchivedKeys.actions,
    queryFn: () =>
      apiClient
        .get<WorkflowActionTaxonomyDto[]>(`${ROUTE}/actions`, {
          params: { includeInactive: true },
        })
        .then((r) => (r.data ?? []).filter((row) => !row.isActive)),
    staleTime: STALE_MS,
  });
}

/** DELETE .../states/{id} with body { confirmCode, reason } — soft-delete (IsActive=false). */
export function useDeleteWorkflowStateTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: DeleteTaxonomyPayload) =>
      apiClient
        .delete<{ referencedByDefinitionCount: number }>(`${ROUTE}/states/${id}`, { data: body })
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.states });
      qc.invalidateQueries({ queryKey: workflowTaxonomyArchivedKeys.states });
    },
  });
}

/** DELETE .../actions/{id} with body { confirmCode, reason } — soft-delete. */
export function useDeleteWorkflowActionTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: DeleteTaxonomyPayload) =>
      apiClient
        .delete<{ referencedByDefinitionCount: number }>(`${ROUTE}/actions/${id}`, { data: body })
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.actions });
      qc.invalidateQueries({ queryKey: workflowTaxonomyArchivedKeys.actions });
    },
  });
}

/** POST .../states/{id}/restore — reactivate a deprecated row (plan D7/R6). */
export function useRestoreWorkflowStateTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post<void>(`${ROUTE}/states/${id}/restore`).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.states });
      qc.invalidateQueries({ queryKey: workflowTaxonomyArchivedKeys.states });
    },
  });
}

/** POST .../actions/{id}/restore — reactivate a deprecated row. */
export function useRestoreWorkflowActionTaxonomy() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post<void>(`${ROUTE}/actions/${id}/restore`).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: workflowTaxonomyKeys.actions });
      qc.invalidateQueries({ queryKey: workflowTaxonomyArchivedKeys.actions });
    },
  });
}
