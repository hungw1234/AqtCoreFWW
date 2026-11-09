/**
 * form-submission-signature.test.ts — DigitalSignature threading contract.
 *
 * Locks: (1) the signature proof bundle is lifted to body.signature, NOT persisted in `data`;
 * (2) cleanFormData drops the DigitalSignature field value (it is not form data).
 */

import { describe, it, expect } from 'vitest';
import { cleanFormData, extractSignaturePayload } from '../form-submission-helpers';
import type { FormFieldDto, SignaturePayloadRequest } from '../form-types';

function f(fieldKey: string, type: FormFieldDto['type'], isActive = true): FormFieldDto {
  return {
    id: fieldKey, fieldKey, type, labelVi: fieldKey, labelEn: fieldKey,
    required: false, displayOrder: 1, isActive, addedInVersion: 1,
  };
}

const SIG: SignaturePayloadRequest = {
  signatureBase64: 'U0lH', signerCertBase64: 'Q0VSVA==',
  format: 'Raw', hashAlg: 'SHA256', encode: 'RSA', providerKey: 'usb-vgca',
};

describe('extractSignaturePayload', () => {
  const fields = [f('name', 'Text'), f('sig', 'DigitalSignature')];

  it('returns the bundle stored under the DigitalSignature field key', () => {
    const out = extractSignaturePayload({ name: 'Alice', sig: SIG }, fields);
    expect(out).toEqual(SIG);
  });

  it('returns null when the field has not been signed', () => {
    expect(extractSignaturePayload({ name: 'Alice' }, fields)).toBeNull();
    expect(extractSignaturePayload({ name: 'Alice', sig: null }, fields)).toBeNull();
    expect(extractSignaturePayload({ name: 'Alice', sig: {} }, fields)).toBeNull();
  });

  it('returns null when template has no DigitalSignature field', () => {
    expect(extractSignaturePayload({ name: 'Alice' }, [f('name', 'Text')])).toBeNull();
  });

  it('ignores an inactive DigitalSignature field', () => {
    const out = extractSignaturePayload(
      { sig: SIG },
      [f('sig', 'DigitalSignature', false)],
    );
    expect(out).toBeNull();
  });
});

describe('cleanFormData — DigitalSignature exclusion', () => {
  it('drops the DigitalSignature value from persisted data', () => {
    const fields = [f('name', 'Text'), f('sig', 'DigitalSignature')];
    const cleaned = cleanFormData({ name: 'Alice', sig: SIG }, fields, { name: 'Alice', sig: SIG });
    expect(cleaned).toEqual({ name: 'Alice' });
    expect(cleaned).not.toHaveProperty('sig');
  });
});
