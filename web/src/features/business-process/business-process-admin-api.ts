// business-process-admin-api.ts — admin API wrappers for BP Definition CRUD
// NOTE: Orval returns void for all BP endpoints. We declare local DTOs and use Path B TData overrides.
// BpDefinitionDto, BpStateFormMapDto, BpTransitionFormMapDto from business-process-run-api.ts are reused.

import { useMutation, useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import {
  useGetApiV1BusinessProcesses,
  useGetApiV1BusinessProcessesId,
  usePostApiV1BusinessProcesses,
  usePutApiV1BusinessProcessesId,
  usePostApiV1BusinessProcessesIdActivate,
  usePostApiV1BusinessProcessesIdDeactivate,
  getGetApiV1BusinessProcessesQueryKey,
  getGetApiV1BusinessProcessesIdQueryKey,
} from '@/api/generated/business-processes/business-processes';
import { useGetApiV1WorkflowDefinitions } from '@/api/generated/workflow-definitions/workflow-definitions';
import { useGetApiV1FormsTemplates } from '@/api/generated/form-templates/form-templates';

export type {
  BpDefinitionDto,
  BpStateFormMapDto,
  BpTransitionFormMapDto,
} from './business-process-run-api';

// ─── Paged list DTO ───────────────────────────────────────────────────────────

export interface BpListItemDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  workflowDefinitionId: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface PaginationMeta {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface BpPagedResponseDto {
  items: BpListItemDto[];
  meta: { pagination: PaginationMeta };
}

// ─── Workflow definition picker DTO ──────────────────────────────────────────
// Mirrors BE WorkflowDefinitionSummaryDto (Workflow.Application/Queries/GetWorkflowDefinitions).
// Id IS the public Guid AK — there is no separate publicId field. The "code"-like field is
// DefinitionKey (BE has no Code property on this DTO).

export interface WorkflowDefinitionPickerDto {
  id: string;
  name: string;
  definitionKey: string;
}

export interface WorkflowDefinitionPagedDto {
  items: WorkflowDefinitionPickerDto[];
  meta: { pagination: PaginationMeta };
}

// ─── Form template picker DTO ─────────────────────────────────────────────────
// Mirrors BE FormTemplateDto (Forms.Application/DTOs/FormTemplateDto.cs). Per its xml-doc,
// Id IS FormTemplate.PublicId (Guid AK) — no separate publicId field. Version is the
// "latest version" the picker exposes for downstream form rendering.

export interface FormTemplatePickerDto {
  id: string;
  name: string;
  code: string;
  version: number;
}

export interface FormTemplatePagedDto {
  items: FormTemplatePickerDto[];
  meta: { pagination: PaginationMeta };
}

// ─── Query key exports (for mutation invalidation) ───────────────────────────

export { getGetApiV1BusinessProcessesQueryKey, getGetApiV1BusinessProcessesIdQueryKey };

// ─── Typed hook wrappers ──────────────────────────────────────────────────────

export function useBpList(params?: {
  search?: string;
  isActive?: boolean;
  pageNumber?: number;
  pageSize?: number;
}) {
  return useGetApiV1BusinessProcesses<BpPagedResponseDto>(params);
}

export function useBpById(id: string) {
  return useGetApiV1BusinessProcessesId<import('./business-process-run-api').BpDefinitionDto>(id, {
    query: { enabled: !!id },
  });
}

export const useCreateBp = usePostApiV1BusinessProcesses;
export const useUpdateBp = usePutApiV1BusinessProcessesId;
export const useActivateBp = usePostApiV1BusinessProcessesIdActivate;
export const useDeactivateBp = usePostApiV1BusinessProcessesIdDeactivate;

// P7-FU1.1: pageSize=500 soft cap. Form-map designer back-fill needs ALL templates in one
// query to resolve bigint formTemplateId → publicId on existing rows. BE has no max-pageSize
// validator so 500 is safe; if a tenant exceeds this, the back-fill drops the row's selection
// and the admin must re-pick. File a paginated-loop follow-up if 500-template tenants emerge.
const PICKER_PAGE_SIZE = 500;

export function useWorkflowDefinitionPicker() {
  return useGetApiV1WorkflowDefinitions<WorkflowDefinitionPagedDto>(
    { pageSize: PICKER_PAGE_SIZE },
    { query: { staleTime: 60_000 } },
  );
}

export function useFormTemplatePicker() {
  return useGetApiV1FormsTemplates<FormTemplatePagedDto>(
    { pageSize: PICKER_PAGE_SIZE },
    { query: { staleTime: 60_000 } },
  );
}

// ─── Form map replace (P7-FU1) ───────────────────────────────────────────────

/** State-form binding sent to PUT /business-processes/{id}/form-maps */
export interface StateFormBindingPayload {
  workflowStateId: number | string;
  formTemplatePublicId: string;
  version: number;
  isRequired: boolean;
}

/** Transition-form binding sent to PUT /business-processes/{id}/form-maps */
export interface TransitionFormBindingPayload {
  workflowTransitionId: number | string;
  formTemplatePublicId: string;
  version: number;
  isRequired: boolean;
}

/** PUT body for replace-all form-map operation */
export interface ReplaceFormMapsPayload {
  stateForms: StateFormBindingPayload[];
  transitionForms: TransitionFormBindingPayload[];
}

/**
 * Mutation hook for atomically replacing all state-form + transition-form bindings.
 * Requires `ifMatchETag` (bp.rowVersion from GET) as If-Match header for optimistic concurrency.
 * Returns 412 PreconditionFailed on stale — caller must handle.
 */
export function useReplaceFormMaps() {
  return useMutation({
    mutationFn: ({
      id,
      data,
      ifMatchETag,
    }: {
      id: string;
      data: ReplaceFormMapsPayload;
      ifMatchETag: string;
    }) =>
      apiClient.put(`/business-processes/${id}/form-maps`, data, {
        headers: { 'If-Match': ifMatchETag },
      }),
  });
}

// ─── BP Reference DTOs (P7-FU2/FU3) ─────────────────────────────────────────

/** Identifies a BP that references a given FormTemplate or WorkflowDefinition. */
export interface BusinessProcessReferenceDto {
  businessProcessPublicId: string;
  code: string;
  name: string;
  /** "intake" | "state-form" | "transition-form" | "workflow" */
  role: string;
  workflowStateId: number | null;
  workflowTransitionId: number | null;
  isActive: boolean;
}

// ─── BP Reference Query Hooks (P7-FU2/FU3) ───────────────────────────────────
// These will be replaced by Orval-generated versions after next API regen.

/**
 * Pre-flight check: lists BPs referencing a FormTemplate (by public Guid — matches FormTemplateDto.id).
 * FE uses this before deactivating/deleting a form template to show a blocking dialog.
 * Enabled only when formTemplatePublicId is a non-empty string.
 */
export function useBpReferencesForFormTemplate(formTemplatePublicId: string | null | undefined) {
  return useQuery({
    queryKey: ['bp', 'references', 'form-template', formTemplatePublicId],
    queryFn: () =>
      apiClient
        .get<BusinessProcessReferenceDto[]>(
          `/business-processes/references/form-template/${formTemplatePublicId}`,
        )
        .then((r) => {
          // API returns ApiResponse envelope: { data: [...] } or plain array — normalise
          const payload = r.data as BusinessProcessReferenceDto[] | { data: BusinessProcessReferenceDto[] };
          return Array.isArray(payload) ? payload : (payload as { data: BusinessProcessReferenceDto[] }).data ?? [];
        }),
    enabled: Boolean(formTemplatePublicId),
    staleTime: 0, // always fresh — pre-flight check must not use cache
  });
}

/**
 * Pre-flight check: lists BPs referencing a WorkflowDefinition (by public Guid — matches WorkflowDefinitionListDto.id).
 * FE uses this before deactivating/deleting a workflow definition to show a blocking dialog.
 * Enabled only when workflowDefinitionPublicId is a non-empty string.
 */
export function useBpReferencesForWorkflowDefinition(workflowDefinitionPublicId: string | null | undefined) {
  return useQuery({
    queryKey: ['bp', 'references', 'workflow-definition', workflowDefinitionPublicId],
    queryFn: () =>
      apiClient
        .get<BusinessProcessReferenceDto[]>(
          `/business-processes/references/workflow-definition/${workflowDefinitionPublicId}`,
        )
        .then((r) => {
          const payload = r.data as BusinessProcessReferenceDto[] | { data: BusinessProcessReferenceDto[] };
          return Array.isArray(payload) ? payload : (payload as { data: BusinessProcessReferenceDto[] }).data ?? [];
        }),
    enabled: Boolean(workflowDefinitionPublicId),
    staleTime: 0,
  });
}
