// Forms feature types — mirrors backend DTOs

// Matches backend FormFieldType enum — grouped by phase
export type FormFieldType =
  // Basic (Phase 1)
  | 'Text'
  | 'Number'
  | 'Date'
  | 'Textarea'
  | 'Boolean'
  | 'File'
  | 'Signature'
  | 'RichText'
  | 'DigitalSignature'
  // Reference (Phase 2)
  | 'EnumRef'
  | 'InternalRef'
  | 'ExternalRef'
  // Complex (Phase 2)
  | 'TableField'
  | 'AddressField'
  | 'DateRange'
  // Computed (Phase 2)
  | 'Formula'
  // UI-only (Phase 2)
  | 'Section'
  | 'Label'
  | 'Divider';

// Matches backend SubmissionStatus enum
export type SubmissionStatus = 'Pending' | 'Approved' | 'Rejected';

/**
 * DigitalSignature signing mode — stored in FormField.validationRulesJson as
 * {"signatureMode":"..."}. Mirrors BE SignatureMode enum. Default (absent/unparseable) =
 * CanonicalJson, matching BE SignatureModeParser.
 */
export type SignatureMode = 'CanonicalJson' | 'ServerDocHash';

/**
 * FE→BE signature proof bundle. Wire shape mirrors BE SignaturePayloadRequest (camelCase);
 * rides as an optional `signature` sibling on the submit body, NOT inside `data`.
 * Mode is resolved server-side from the field config — never sent by the client.
 */
export interface SignaturePayloadRequest {
  signatureBase64: string;
  signerCertBase64?: string | null;
  /** 'Raw' (VGCA USB / Viettel — cert supplied separately) or 'Pkcs7' (CMS envelope). */
  format?: string;
  hashAlg?: string;
  encode?: string;
  providerKey?: string | null;
}

export interface FormFieldOptionDto {
  id: string;
  value: string;
  labelVi: string;
  labelEn: string;
  displayOrder: number;
}

export interface FormFieldValidationRulesDto {
  minLength?: number | null;
  maxLength?: number | null;
  min?: number | null;
  max?: number | null;
  pattern?: string | null;
}

export interface FormFieldDto {
  id: string;
  fieldKey: string;
  type: FormFieldType;
  labelVi: string;
  labelEn: string;
  required: boolean;
  displayOrder: number;
  isActive: boolean;
  addedInVersion: number;
  validationRules?: FormFieldValidationRulesDto | null;
  options?: FormFieldOptionDto[] | null;
  // Raw JSON strings for builder editors
  dataSourceJson?: string | null;
  formulaJson?: string | null;
  validationRulesJson?: string | null;
  /** JSON array of visibility conditions — evaluated client-side in preview */
  visibilityRulesJson?: string | null;
  /** JSON array of required-if conditions — field becomes required when conditions are met */
  requiredIfJson?: string | null;
  isPii?: boolean;
  /**
   * Internal FK to parent FormField.Id (bigint T1). Non-null for TableField child columns; null (or
   * absent) for top-level fields. FE `TableFieldWidget` uses this to discover child columns by id
   * instead of the legacy `fieldKey` prefix convention (blocked in production by AddFormField regex
   * that forbids dots). Wire shape mirrors BE `long?` = JSON `number | null`.
   */
  parentFieldId?: number | null;
}

/** Single visibility condition: show this field when [fieldKey] [op] [value] */
export interface VisibilityCondition {
  fieldKey: string;
  operator: 'eq' | 'neq' | 'gt' | 'lt' | 'gte' | 'lte' | 'contains' | 'empty' | 'notEmpty';
  value?: string;
}

/** Top-level visibility rules wrapper */
export interface VisibilityRules {
  /** 'all' = AND, 'any' = OR */
  match: 'all' | 'any';
  conditions: VisibilityCondition[];
}

// Matches backend FormTemplateStatus enum
export type FormStatus = 'Draft' | 'Materializing' | 'Active' | 'Inactive';

export interface FormTemplateDto {
  id: string;
  code: string;
  name: string;
  nameVi?: string;
  description?: string;
  status: FormStatus;
  version: number;
  fields: FormFieldDto[];
  submissionsCount: number;
  createdAt: string;
  updatedAt?: string;
  requiresConsent?: boolean;
  consentText?: string | null;
  approvalWorkflowDefinitionId?: string | null;
  /** Anonymous public reachability (RLS public-read carve-out). Only single-template GET populates it. */
  isPublic?: boolean;
}

export interface FormSubmissionDto {
  id: string;
  tenantId: string;
  formTemplateId: string;
  formTemplateVersion: number;
  submittedBy: string;
  submittedAt: string;
  data: Record<string, unknown>;
  status: SubmissionStatus;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  reviewComment?: string | null;
}

export interface FormSubmissionListItemDto {
  id: string;
  formTemplateId: string;
  formTemplateVersion: number;
  submittedBy: string;
  submittedAt: string;
  status: SubmissionStatus;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  reviewComment?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Payload for POST /templates/{id}/fields */
export interface AddFieldPayload {
  fieldKey: string;
  type: FormFieldType;
  labelVi: string;
  labelEn: string;
  displayOrder: number;
  required: boolean;
  validationRulesJson?: string | null;
  options?: FieldOptionPayload[] | null;
  dataSourceJson?: string | null;
  formulaJson?: string | null;
}

/** Payload for PUT /templates/{id}/fields/{fieldId} */
export interface UpdateFieldPayload {
  labelVi?: string | null;
  labelEn?: string | null;
  required?: boolean | null;
  validationRulesJson?: string | null;
  dataSourceJson?: string | null;
  formulaJson?: string | null;
  isPii?: boolean | null;
  visibilityRulesJson?: string | null;
  /** JSON array of required-if conditions — field becomes conditionally required */
  requiredIfJson?: string | null;
}

export interface FieldOptionPayload {
  value: string;
  labelVi: string;
  labelEn: string;
  displayOrder: number;
}

// --- Analytics ---
export interface FormAnalyticsDto {
  totalSubmissions: number;
  pendingCount: number;
  approvedCount: number;
  rejectedCount: number;
  avgFieldsFilled: number;
  submissionsByDate: Array<{ date: string; count: number }>;
}

// --- Version diff ---
export interface DiffFieldDto {
  fieldKey: string;
  labelVi: string;
  labelEn: string;
  addedInVersion: number;
  isActive: boolean;
}

export interface FormVersionDiffDto {
  fromVersion: number;
  toVersion: number;
  added: DiffFieldDto[];
  removed: DiffFieldDto[];
  modified: DiffFieldDto[];
}

// --- Bulk actions ---
export interface BulkActionResult {
  succeeded: number;
  failed: number;
  errors: string[];
}

// --- Submission field filter ---
export interface SubmissionFieldFilter {
  fieldKey: string;
  value: string;
}

// --- DataSource dynamic options ---
export interface DataSourceOptionDto {
  value: string;
  label: string;
  labelVi?: string;
}

/**
 * Batch formula-evaluate response — mirrors BE FormulaEvaluationResultDto { Results }.
 * `results` maps each active Formula field's key to its computed value (number/bool/string).
 * BE OMITS keys that compute to null; FE renders blank for a missing key.
 * Both apiClient (authed) and publicClient (public) unwrap the envelope to `.data` = this shape.
 */
export interface FormulaEvaluationResult {
  results: Record<string, unknown>;
}
