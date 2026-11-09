// Rules API hooks — wraps /api/v1/rules/* endpoints

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult, PaginationParams } from '@/shared/types/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export type RuleSetStatus = 'Draft' | 'Active' | 'Deprecated';

export interface RuleDto {
  id: string;
  ruleSetId: string;
  name: string;
  description?: string;
  expression: string;
  priority: number;
  enabled: boolean;
  errorMessage?: string;
  failureAction: string;
}

// Matches BE RuleSetDto — GetRuleSet handler embeds rules list in the detail response.
export interface RuleSetDto {
  id: string;
  name: string;
  description?: string;
  version: number;
  status: RuleSetStatus;
  createdAt: string;
  updatedAt?: string;
  rules: RuleDto[];
}

export interface UpdateRuleDto {
  name?: string;
  expression?: string;
  priority?: number;
  enabled?: boolean;
  failureAction?: string;
}

export interface DecisionTableDto {
  id: string;
  ruleSetId: string;
  name: string;
  inputColumns: string[];
  outputColumns: string[];
  rows: Record<string, unknown>[];
}

export interface CreateRuleSetDto {
  name: string;
  description?: string;
}

export interface TestRuleSetRequest {
  ruleSetId: string;
  inputJson: string;
}

export interface TestRuleSetResponse {
  matched: boolean;
  output?: Record<string, unknown>;
  matchedRules?: string[];
  executionMs?: number;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const rulesQueryKeys = {
  all: ['rules'] as const,
  ruleSets: (params: PaginationParams) => ['rules', 'ruleSets', params] as const,
  ruleSet: (id: string) => ['rules', 'ruleSet', id] as const,
  decisionTables: (ruleSetId: string) => ['rules', 'ruleSet', ruleSetId, 'decision-tables'] as const,
};

// ─── RuleSet queries ──────────────────────────────────────────────────────────

/** GET /api/v1/rules/rule-sets — paginated list */
export function useRuleSets(params: PaginationParams = {}) {
  return useQuery({
    queryKey: rulesQueryKeys.ruleSets(params),
    queryFn: () =>
      apiClient.get<PaginatedResult<RuleSetDto>>('/rules/rule-sets', { params }).then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

/** GET /api/v1/rules/rule-sets/{id} — full detail, including embedded rules list. */
export function useRuleSet(id: string) {
  return useQuery({
    queryKey: rulesQueryKeys.ruleSet(id),
    queryFn: () => apiClient.get<RuleSetDto>(`/rules/rule-sets/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

/** GET /api/v1/rules/rule-sets/{id}/decision-tables — flat list; small per rule-set. */
export function useDecisionTables(ruleSetId: string) {
  return useQuery({
    queryKey: rulesQueryKeys.decisionTables(ruleSetId),
    queryFn: () =>
      apiClient
        .get<DecisionTableDto[]>(`/rules/rule-sets/${ruleSetId}/decision-tables`)
        .then((r) => r.data),
    enabled: Boolean(ruleSetId),
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/rules/rule-sets */
export function useCreateRuleSet() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRuleSetDto) =>
      apiClient.post<RuleSetDto>('/rules/rule-sets', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: rulesQueryKeys.all }),
  });
}

/** PUT /api/v1/rules/rule-sets/{id} */
export function useUpdateRuleSet() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CreateRuleSetDto }) =>
      apiClient.put<RuleSetDto>(`/rules/rule-sets/${id}`, body).then((r) => r.data),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: rulesQueryKeys.ruleSet(id) });
      qc.invalidateQueries({ queryKey: rulesQueryKeys.all });
    },
  });
}

/** POST /api/v1/rules/rule-sets/{id}/activate */
export function useActivateRuleSet() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/rules/rule-sets/${id}/activate`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: rulesQueryKeys.all }),
  });
}

/** POST /api/v1/rules/rule-sets/{id}/deprecate */
export function useDeprecateRuleSet() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/rules/rule-sets/${id}/deprecate`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: rulesQueryKeys.all }),
  });
}

/** DELETE /api/v1/rules/rule-sets/{id} */
export function useDeleteRuleSet() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/rules/rule-sets/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: rulesQueryKeys.all }),
  });
}

/** POST /api/v1/rules/test — evaluate rule set against input */
export function useTestRuleSet() {
  return useMutation({
    mutationFn: (body: TestRuleSetRequest) =>
      apiClient.post<TestRuleSetResponse>('/rules/test', body).then((r) => r.data),
  });
}

/** PUT /api/v1/rules/rule-sets/{ruleSetId}/rules/{ruleId} */
export function useUpdateRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ ruleSetId, ruleId, body }: { ruleSetId: string; ruleId: string; body: UpdateRuleDto }) =>
      apiClient
        .put<RuleDto>(`/rules/rule-sets/${ruleSetId}/rules/${ruleId}`, body)
        .then((r) => r.data),
    // Rules are embedded in the RuleSet detail — invalidate that key to refetch.
    onSuccess: (_d, { ruleSetId }) => {
      qc.invalidateQueries({ queryKey: rulesQueryKeys.ruleSet(ruleSetId) });
    },
  });
}
