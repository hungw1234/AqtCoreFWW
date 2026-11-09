/**
 * form-data-canonicalizer.ts — FE half of the Mode-1 (CanonicalJson) byte contract.
 *
 * MUST produce UTF-8 bytes byte-identical to BE `FormDataCanonicalizer.CanonicalizeMode1`
 * (src/modules/forms/AqtCoreFW.Forms.Application/Services/FormDataCanonicalizer.cs). Any drift
 * causes a 100% false-reject at verify-at-ingest. The parity guard is
 * `form-data-canonicalizer.test.ts`, which pins the SAME fixed vectors as the BE test
 * `FormDataCanonicalizerTests.cs`.
 *
 * INPUT CONTRACT — values must already be JSON-normalized primitives (string | number |
 * boolean | null), i.e. exactly what the server will deserialize from the request body. Callers
 * MUST round-trip the live form values through `JSON.parse(JSON.stringify(...))` BEFORE calling
 * this so antd value types (e.g. dayjs Date objects) collapse to the same ISO strings the BE
 * receives. Passing raw antd values would diverge (dayjs.toString() !== its JSON ISO form).
 *
 * Rules (mirror of BE):
 *  - Covered keys: present in data AND map to an active scalar field (see SCALAR_FIELD_TYPES).
 *  - Null/undefined covered values: key omitted entirely (never serialized as null).
 *  - Coercion: string verbatim; number → String(n) (=== its JSON repr); boolean → "true"/"false".
 *  - NFC-normalize every value string.
 *  - Ordinal key sort (Array.prototype.sort default = UTF-16 code-unit order = ordinal for ASCII keys).
 *  - Compact JSON.stringify (no whitespace); non-ASCII left literal (matches BE
 *    UnsafeRelaxedJsonEscaping); UTF-8 encode.
 *
 * Versioning: v1. Any change to the covered set, coercion, ordering, or encoding is a
 * breaking change that invalidates existing signatures — bump the spec before changing.
 */

import type { FormFieldType } from '../forms/form-types';

/**
 * Scalar field types whose values are included in the signed canonical payload.
 * Exact mirror of BE `FormDataCanonicalizer.ScalarFieldTypes`. Excluded (never signed):
 * File, Signature (PNG), DigitalSignature, Formula, Section/Label/Divider, TableField,
 * AddressField, DateRange.
 */
const SCALAR_FIELD_TYPES: ReadonlySet<FormFieldType> = new Set<FormFieldType>([
  'Text',
  'Number',
  'Date',
  'Textarea',
  'Boolean',
  'RichText',
  'EnumRef',
  'InternalRef',
  'ExternalRef',
]);

/** Minimal field shape needed for covered-field selection. */
export interface CanonicalField {
  fieldKey: string;
  type: FormFieldType;
  isActive: boolean;
}

/**
 * Coerce a JSON-normalized value to its canonical string form.
 * Returns null only for null/undefined (caller omits the key).
 */
function coerceToString(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string') return value;
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (typeof value === 'number') return String(value);
  // Residual non-primitive — unreachable for covered scalar fields, but if one ever arrives,
  // mirror BE CoerceToString (a non-string JsonElement uses GetRawText() = compact JSON) so the
  // bytes still match rather than emitting "[object Object]" and silently false-rejecting.
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

/**
 * Build the canonical ordinal-sorted plain object of covered scalar fields.
 * Exposed for reuse by the Mode-2 wrapper builder (keeps the field-selection rule single-source).
 */
export function buildCanonicalDataObject(
  data: Record<string, unknown>,
  activeFields: CanonicalField[],
): Record<string, string> {
  const scalarKeys = new Set<string>(
    activeFields.filter((f) => f.isActive && SCALAR_FIELD_TYPES.has(f.type)).map((f) => f.fieldKey),
  );

  const collected: Record<string, string> = {};
  for (const key of Object.keys(data)) {
    if (!scalarKeys.has(key)) continue;
    const coerced = coerceToString(data[key]);
    if (coerced === null) continue; // null/undefined → omit
    collected[key] = coerced.normalize('NFC');
  }

  // Ordinal key sort, then rebuild in sorted order so JSON.stringify emits ordinal order.
  const sorted: Record<string, string> = {};
  for (const key of Object.keys(collected).sort()) {
    sorted[key] = collected[key];
  }
  return sorted;
}

/**
 * Produce the canonical UTF-8 bytes for a Mode-1 (CanonicalJson) signature payload.
 * Byte-identical to BE `CanonicalizeMode1`.
 */
export function canonicalizeMode1(
  data: Record<string, unknown>,
  activeFields: CanonicalField[],
): Uint8Array {
  const canonical = buildCanonicalDataObject(data, activeFields);
  // JSON.stringify on a plain object is compact (no whitespace) and leaves non-ASCII literal
  // in modern engines — matches BE UnsafeRelaxedJsonEscaping.
  const json = JSON.stringify(canonical);
  return new TextEncoder().encode(json);
}
