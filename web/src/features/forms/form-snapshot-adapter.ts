// form-snapshot-adapter.ts — maps the published FormTemplateVersion *snapshot* envelope
// (served by GET intake-form / state-form-map schema endpoints) into the FormFieldDto shape
// the interactive renderer + form helpers expect.
//
// The snapshot is a DISTINCT serialization from the FormField entity DTO: it renames keys
// (key→fieldKey, validation→validationRulesJson, visibility→visibilityRulesJson,
// requiredIf→requiredIfJson) and omits isActive entirely because FormTemplateVersionFactory
// only snapshots already-active fields. Consuming the snapshot as a raw FormFieldDto silently
// drops every field (isActive undefined → filtered out) and unbinds inputs (fieldKey undefined).

import type { FormFieldDto, FormFieldOptionDto, FormFieldType } from './form-types';

/** Option shape inside a snapshot field's `options` array (camelCase serialized FormFieldOption). */
interface SnapshotOption {
  id: number | string;
  value: string;
  labelVi: string;
  labelEn: string;
  displayOrder: number;
}

/** Field shape inside a published version-snapshot's schemaJson `fields` array. */
interface SnapshotField {
  id: number | string;
  key: string;
  type: FormFieldType;
  labelVi: string;
  labelEn: string;
  displayOrder: number;
  required: boolean;
  isPii?: boolean;
  validation?: string | null;
  visibility?: string | null;
  requiredIf?: string | null;
  // Additive-nullable: absent on older snapshots and on fields without choices.
  options?: SnapshotOption[] | null;
}

function mapSnapshotField(f: SnapshotField): FormFieldDto {
  return {
    id: String(f.id),
    fieldKey: f.key,
    type: f.type,
    labelVi: f.labelVi,
    labelEn: f.labelEn,
    required: f.required,
    displayOrder: f.displayOrder,
    // Snapshot serializes only active fields, so every field is active by construction.
    isActive: true,
    // Not carried in the snapshot; unused by the interactive renderer.
    addedInVersion: 0,
    validationRulesJson: f.validation ?? null,
    visibilityRulesJson: f.visibility ?? null,
    requiredIfJson: f.requiredIf ?? null,
    isPii: f.isPii ?? false,
    // Inline EnumRef/Radio choices (renderInteractiveField reads field.options for EnumRef).
    // Null when the snapshot predates option-carrying or the field has none.
    options: f.options?.map(mapSnapshotOption) ?? null,
  };
}

function mapSnapshotOption(o: SnapshotOption): FormFieldOptionDto {
  return {
    id: String(o.id),
    value: o.value,
    labelVi: o.labelVi,
    labelEn: o.labelEn,
    displayOrder: o.displayOrder,
  };
}

/**
 * Parse a version-snapshot `schemaJson` string into renderer-ready FormFieldDto[].
 * Accepts both the enveloped form (`{ fields: [...] }`) and a bare fields array.
 * Returns [] on malformed JSON (caller renders an empty form rather than crashing).
 */
export function parseSnapshotSchema(schemaJson: string): FormFieldDto[] {
  let parsed: unknown;
  try {
    parsed = JSON.parse(schemaJson);
  } catch {
    return [];
  }

  const rawFields = Array.isArray(parsed)
    ? parsed
    : ((parsed as { fields?: unknown[] } | null)?.fields ?? []);

  return (rawFields as SnapshotField[]).map(mapSnapshotField);
}
