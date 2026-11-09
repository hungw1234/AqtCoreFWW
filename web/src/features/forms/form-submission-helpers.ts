// form-submission-helpers.ts — clean form data before submission
// Strips hidden fields (visibility), UI-only types, and internal keys (__rowKey)

import type { FormFieldDto, SignaturePayloadRequest } from './form-types';
import { isFieldVisible } from './form-visibility-helpers';

const UI_ONLY_TYPES = new Set(['Section', 'Label', 'Divider', 'Formula']);

/**
 * Clean form values before API submission:
 * 1. Skip UI-only field types (Section, Label, Divider, Formula)
 * 2. Skip hidden fields (visibility rules evaluate to false)
 * 3. Strip __rowKey from TableField row arrays
 * 4. Skip inactive fields
 */
export function cleanFormData(
  values: Record<string, unknown>,
  fields: FormFieldDto[],
  formValues: Record<string, unknown>
): Record<string, unknown> {
  const cleaned: Record<string, unknown> = {};

  for (const field of fields) {
    if (!field.isActive) continue;
    if (UI_ONLY_TYPES.has(field.type)) continue;
    // DigitalSignature value is the signature proof bundle, not form data — it travels in the
    // body.signature sibling (see extractSignaturePayload), never persisted inside `data`.
    // BE excludes the type from canonicalization too, so dropping it does not affect byte-parity.
    if (field.type === 'DigitalSignature') continue;
    if (!isFieldVisible(field.visibilityRulesJson, formValues)) continue;

    const val = values[field.fieldKey];
    if (val === undefined) continue;

    // Strip __rowKey from TableField rows
    if (field.type === 'TableField' && Array.isArray(val)) {
      cleaned[field.fieldKey] = (val as Record<string, unknown>[]).map(
        ({ __rowKey: _, ...rest }) => rest
      );
    } else {
      cleaned[field.fieldKey] = val;
    }
  }
  return cleaned;
}

/**
 * Lift the captured signature proof bundle out of form values for the submit body's `signature`
 * sibling. Returns the SignaturePayloadRequest stored by DigitalSignatureWidget under its field
 * key, or null when there is no DigitalSignature field or it has not been signed.
 *
 * Only the first active DigitalSignature field is considered — a template carries at most one
 * (multiple signed payloads on a single submission is out of scope; BE verifies a single bundle).
 */
export function extractSignaturePayload(
  values: Record<string, unknown>,
  fields: FormFieldDto[],
): SignaturePayloadRequest | null {
  const sigField = fields.find((f) => f.isActive && f.type === 'DigitalSignature');
  if (!sigField) return null;
  const raw = values[sigField.fieldKey];
  if (!raw || typeof raw !== 'object') return null;
  const payload = raw as Partial<SignaturePayloadRequest>;
  return typeof payload.signatureBase64 === 'string' && payload.signatureBase64.length > 0
    ? (payload as SignaturePayloadRequest)
    : null;
}
