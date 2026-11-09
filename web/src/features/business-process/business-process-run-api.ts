// business-process-run-api.ts — response DTOs + thin Orval hook wrappers for BP Run feature
// NOTE: Orval generates all endpoints as apiClientMutator<void> (no response type in OpenAPI).
// We declare local response types here and override TData at Orval hook call-sites (Path B).
// This is the ONLY file in the BP Run feature that contains local DTOs — deviation documented.

import { useMutation, useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import {
  useGetApiV1BusinessProcessesActive,
  useGetApiV1BusinessProcessesId,
  useGetApiV1BusinessProcessesIdIntakeForm,
  useGetApiV1BusinessProcessesRunsWorkflowInstanceIdFormsFormTemplatePublicIdPrefillContext,
} from '@/api/generated/business-processes/business-processes';
import type { StartBusinessProcessRunRequest } from '@/api/generated/aqtCoreFWAPIV1.schemas';
import type { FormFieldDto, SignaturePayloadRequest } from '@/features/forms/form-types';

// ─── Response DTOs (local — Orval OpenAPI omits response bodies) ───────────────

/** Matches BusinessProcessListItemDto from BE */
export interface BpActiveDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  workflowDefinitionId: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

/** Matches BusinessProcessStateFormDto from BE — bigint ids, no fields inline */
export interface BpStateFormMapDto {
  id: string;
  workflowStateId: number;
  formTemplateId: number;
  /**
   * T2 Guid AK of the bound form template, resolved server-side (mirrors
   * workflowDefinitionPublicId). The edit picker binds directly to this; the FE cannot
   * resolve the internal bigint formTemplateId itself. Nullable: orphan FK tolerated.
   */
  formTemplatePublicId?: string | null;
  formTemplateVersion: number;
  isRequired: boolean;
  displayOrder: number;
}

/** Matches BusinessProcessTransitionFormDto from BE */
export interface BpTransitionFormMapDto {
  id: string;
  workflowTransitionId: number;
  formTemplateId: number;
  /** T2 Guid AK of the bound form template, resolved server-side. See BpStateFormMapDto. */
  formTemplatePublicId?: string | null;
  formTemplateVersion: number;
  isRequired: boolean;
  displayOrder: number;
}

/** Matches BusinessProcessDetailDto from BE */
export interface BpDefinitionDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  workflowDefinitionId: number;
  /**
   * P7-FU1.2: Guid AK of the workflow definition — FE form-map designer uses this to call
   * GET /api/v1/workflow/definitions/{id:guid} for state/transition display names (picker options).
   * Nullable: BE returns null if workflow row hard-deleted (orphan FK).
   */
  workflowDefinitionPublicId?: string | null;
  intakeFormTemplateId?: number | null;
  intakeFormTemplateVersion?: number | null;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
  stateForms: BpStateFormMapDto[];
  transitionForms: BpTransitionFormMapDto[];
  /** Base64-encoded SQL rowversion — send as If-Match header to ReplaceFormMaps endpoint */
  rowVersion: string;
}

/**
 * Intake form snapshot from BE: { FormTemplatePublicId, VersionNumber, SchemaJson }
 * SchemaJson is a raw JSON string containing the form template fields array.
 */
export interface BpIntakeFormRawDto {
  formTemplatePublicId: string;
  versionNumber: number;
  schemaJson: string;
}

/** Parsed intake form — schemaJson decoded to fields array */
export interface BpIntakeFormDto {
  formTemplatePublicId: string;
  versionNumber: number;
  fields: FormFieldDto[];
}

/**
 * Wire shape of one required transition form (BE BpRequiredTransitionForm). Carries the published
 * version-snapshot string — same envelope as intake-form / state-form. The FE parses schemaJson into
 * FormFieldDto[] via parseSnapshotSchema before handing it to the modal.
 */
export interface BpRequiredTransitionFormRaw {
  transitionFormTemplateId: number | string;
  formTemplatePublicId: string;
  schemaJson: string;
}

/** Discriminated response from POST /runs/{id}/transitions */
export interface BpTransitionRequiresForms {
  requiresForms: true;
  transitionForms: BpRequiredTransitionFormRaw[];
}

/** Parsed transition form consumed by TransitionConfirmModal (schemaJson decoded to fields). */
export interface BpTransitionFormWithSnapshot {
  transitionFormTemplateId: number | string;
  formTemplatePublicId: string;
  fields: FormFieldDto[];
}

export interface BpTransitionSuccess {
  success: true;
  newStateId?: string | number | null;
  newStateName?: string | null;
}

export type BpTransitionResponse = BpTransitionRequiresForms | BpTransitionSuccess;

/** Prefill context dict returned by P6 endpoint */
export type PrefillContextDto = Record<string, unknown>;

// ─── Typed hook wrappers ──────────────────────────────────────────────────────

/** Active BP list for the run picker (max 100, BE cap). BE returns a PagedResult
 *  ({ items, meta }), NOT a bare array — the prior `<BpActiveDto[]>` cast was a lie
 *  that crashed the wizard at `.map`. Picker only needs `items`. */
export function useActiveBps() {
  return useGetApiV1BusinessProcessesActive<{ items: BpActiveDto[] }>();
}

/** Full BP definition including stateForms + transitionForms (disabled when id is empty) */
export function useBpDefinition(id: string) {
  return useGetApiV1BusinessProcessesId<BpDefinitionDto>(id, {
    query: { enabled: !!id },
  });
}

/** Intake form snapshot for a BP (204 → undefined, caller must handle) */
export function useBpIntakeForm(bpId: string, enabled = true) {
  return useGetApiV1BusinessProcessesIdIntakeForm<BpIntakeFormRawDto>(bpId, {
    query: { enabled: !!bpId && enabled },
  });
}

/** Variables for {@link useStartBpRun}. `requestToken` is an anti-double-submit token minted once
 *  per user-initiated submit attempt; re-firing the same token within the AntiDoubleSubmit window
 *  (5s) returns 409, so an accidental re-submit (retry, refresh) cannot create a duplicate run while
 *  two genuinely separate submits (distinct tokens) both succeed. */
export interface StartBpRunVariables {
  id: string;
  // Intersect the generated request with the optional signature sibling — the OpenAPI schema
  // omits it (BE accepts it as an optional body field verified at ingest).
  data: StartBusinessProcessRunRequest & { signature?: SignaturePayloadRequest | null };
  requestToken: string;
}

/**
 * Start a BP run. Hand-wired (not the Orval hook) so the per-attempt `X-Request-Token` header can be
 * attached to THIS POST only — the generated client has no per-call header hook. Returns the raw
 * response body (matches the prior Orval `void` shape the caller already unwraps).
 */
export function useStartBpRun() {
  return useMutation({
    mutationFn: ({ id, data, requestToken }: StartBpRunVariables) =>
      apiClient
        .post<unknown>(`/business-processes/${id}/runs`, data, {
          headers: { 'X-Request-Token': requestToken },
        })
        .then((r) => r.data),
  });
}

/** Request body for {@link useExecuteBpTransition} — mirrors the BE ExecuteBusinessProcessTransitionRequest. */
export interface ExecuteBpTransitionBody {
  bpPublicId: string;
  actionName: string;
  transitionFormTemplateId: number | string | null;
  transitionFormData: Record<string, unknown> | null;
  consentGiven: boolean;
  comment: string | null;
  // Optional digital-signature proof bundle when a bundled transition form carries a DigitalSignature field.
  signature?: SignaturePayloadRequest | null;
}

export interface ExecuteBpTransitionVariables {
  workflowInstanceId: string;
  data: ExecuteBpTransitionBody;
}

/**
 * Execute a BP-Run transition. Hand-wired (not the Orval alias) so the optional `signature` sibling can
 * ride a bundled transition form's body — the generated OpenAPI request schema omits it. Returns the raw
 * discriminated response body ({requiresForms} | {success}) the callers already cast.
 */
export function useExecuteBpTransition() {
  return useMutation({
    mutationFn: ({ workflowInstanceId, data }: ExecuteBpTransitionVariables) =>
      apiClient
        .post<unknown>(`/business-processes/runs/${workflowInstanceId}/transitions`, data)
        .then((r) => r.data),
  });
}

/** Request body for {@link useSubmitBpStateForm} — mirrors the BE SubmitBusinessProcessStateFormRequest. */
export interface SubmitBpStateFormBody {
  bpPublicId: string;
  formTemplateId: number | string;
  data: Record<string, unknown>;
  consentGiven: boolean;
  // Optional digital-signature proof bundle when the state form carries a DigitalSignature field.
  signature?: SignaturePayloadRequest | null;
}

export interface SubmitBpStateFormVariables {
  workflowInstanceId: string;
  stateId: number;
  data: SubmitBpStateFormBody;
}

/**
 * Submit a BP-Run state form. Hand-wired (not the Orval alias) so the optional `signature` sibling
 * can ride the body — the generated OpenAPI request schema omits it. Mirrors the start-run pattern.
 */
export function useSubmitBpStateForm() {
  return useMutation({
    mutationFn: ({ workflowInstanceId, stateId, data }: SubmitBpStateFormVariables) =>
      apiClient
        .post<unknown>(
          `/business-processes/runs/${workflowInstanceId}/state-forms/${stateId}`,
          data,
        )
        .then((r) => r.data),
  });
}

export function usePrefillContextRaw(
  workflowInstanceId: string,
  formTemplatePublicId: string,
  enabled = true,
) {
  return useGetApiV1BusinessProcessesRunsWorkflowInstanceIdFormsFormTemplatePublicIdPrefillContext<PrefillContextDto>(
    workflowInstanceId,
    formTemplatePublicId,
    undefined,
    { query: { enabled: !!workflowInstanceId && !!formTemplatePublicId && enabled } },
  );
}

/** One submitted field assembled server-side: snapshot label (vi/en) + the entered value. */
export interface BpRunSubmissionField {
  key: string;
  /** FormFieldType name (e.g. 'Text', 'File', 'Signature') — drives type-aware read-only rendering. */
  type: string;
  labelVi: string;
  labelEn: string;
  value: unknown;
}

/** Latest submission for one form in a BP-Run. workflowStateId null = intake form. */
export interface BpRunSubmission {
  formTemplatePublicId: string;
  formTemplateVersion: number;
  workflowStateId: number | null;
  submittedAt: string;
  fields: BpRunSubmissionField[];
  /** Submission-level digital-signature evidence (QĐ-742 verify-at-ingest). */
  isSigned?: boolean;
  /** Certificate Common Name of the signer (full DN/IDs kept server-side). */
  signerName?: string | null;
  signatureVerifiedAt?: string | null;
}

/** Query key for the read-only BP-Run submissions list. Exported so call-sites can invalidate it
 *  after a transition / state-form submit without duplicating the literal (kept in lock-step with
 *  the hook below — a transition or state-form submit changes the submitted data this key reads). */
export const bpRunSubmissionsKey = (workflowInstanceId: string) =>
  ['bp', 'run-submissions', workflowInstanceId] as const;

/**
 * Read-only: latest submitted form data for a BP-Run (intake + each state form), rendered on the
 * run detail page. Gated BE-side on BP.View — the page's own permission — so no admin/MFA endpoint
 * is touched. Hand-wired (not Orval) until the next regen picks up the GET endpoint.
 */
export function useBpRunSubmissions(workflowInstanceId: string, enabled = true) {
  return useQuery({
    queryKey: bpRunSubmissionsKey(workflowInstanceId),
    queryFn: () =>
      apiClient
        .get<BpRunSubmission[]>(`/business-processes/runs/${workflowInstanceId}/submissions`)
        .then((r) => {
          const payload = r.data as BpRunSubmission[] | { data: BpRunSubmission[] };
          return ((payload as { data?: BpRunSubmission[] }).data ?? payload) as BpRunSubmission[];
        }),
    enabled: Boolean(workflowInstanceId) && enabled,
  });
}

/**
 * P8-FU1: fetch FormTemplate snapshot for a single state-form map (BP MVP state-form panel).
 * `stateFormMapPublicId` is the Guid AK from BpStateFormMapDto.id (NOT a FormTemplate Guid).
 * Returns BpIntakeFormRawDto shape ({ formTemplatePublicId, versionNumber, schemaJson }) — reused
 * BE DTO. Hand-wired (not Orval) until next regen picks up the GET endpoint.
 * Envelope normalised the same way as useBpReferencesForFormTemplate (admin-api.ts).
 */
export function useBpStateFormSchema(
  bpPublicId: string,
  stateFormMapPublicId: string,
  enabled = true,
) {
  return useQuery({
    queryKey: ['bp', 'state-form-schema', bpPublicId, stateFormMapPublicId],
    queryFn: () =>
      apiClient
        .get<BpIntakeFormRawDto>(
          `/business-processes/${bpPublicId}/state-form-maps/${stateFormMapPublicId}/schema`,
        )
        .then((r) => {
          const payload = r.data as BpIntakeFormRawDto | { data: BpIntakeFormRawDto };
          return ((payload as { data?: BpIntakeFormRawDto }).data ?? payload) as BpIntakeFormRawDto;
        }),
    enabled: Boolean(bpPublicId) && Boolean(stateFormMapPublicId) && enabled,
  });
}
