import { describe, it, expect } from 'vitest';
import { parseSnapshotSchema } from '../form-snapshot-adapter';

// Ground-truth envelope as served by GET /business-processes/{id}/intake-form
// (FormTemplateVersionFactory snapshot shape: key/validation/visibility, no isActive).
const SNAPSHOT_ENVELOPE = JSON.stringify({
  schemaEnvelopeVersion: 1,
  version: 1,
  storageMode: 'Json',
  requiresConsent: false,
  fields: [
    {
      id: 10043,
      key: 'full_name',
      type: 'Text',
      labelVi: 'Họ và tên',
      labelEn: 'Full Name',
      displayOrder: 1,
      required: true,
      isPii: false,
      validation: null,
      visibility: null,
      requiredIf: null,
    },
    {
      id: 10046,
      key: 'request_details',
      type: 'Textarea',
      labelVi: 'Chi tiết yêu cầu',
      labelEn: 'Request Details',
      displayOrder: 4,
      required: true,
      isPii: false,
      validation: '{"maxLength":500}',
      visibility: '[{"fieldKey":"full_name","operator":"notEmpty"}]',
      requiredIf: null,
    },
  ],
});

describe('parseSnapshotSchema', () => {
  it('maps snapshot fields into renderer-ready FormFieldDto (regression: empty intake form)', () => {
    const fields = parseSnapshotSchema(SNAPSHOT_ENVELOPE);

    expect(fields).toHaveLength(2);

    // The bug: snapshot uses `key`/`validation`/`visibility` and omits `isActive`, so the prior
    // raw cast produced fieldKey=undefined + isActive=undefined → every field filtered out.
    const fullName = fields[0];
    expect(fullName.fieldKey).toBe('full_name');
    expect(fullName.isActive).toBe(true);
    expect(fullName.type).toBe('Text');
    expect(fullName.required).toBe(true);

    const details = fields[1];
    expect(details.fieldKey).toBe('request_details');
    expect(details.validationRulesJson).toBe('{"maxLength":500}');
    expect(details.visibilityRulesJson).toBe('[{"fieldKey":"full_name","operator":"notEmpty"}]');

    // Every field must survive the active-filter the renderers apply.
    expect(fields.every((f) => f.isActive)).toBe(true);
  });

  it('accepts a bare fields array (non-enveloped snapshot)', () => {
    const bare = JSON.stringify([
      { id: 1, key: 'a', type: 'Text', labelVi: 'A', labelEn: 'A', displayOrder: 1, required: false },
    ]);
    const fields = parseSnapshotSchema(bare);
    expect(fields).toHaveLength(1);
    expect(fields[0].fieldKey).toBe('a');
    expect(fields[0].isActive).toBe(true);
  });

  it('returns [] on malformed JSON instead of throwing', () => {
    expect(parseSnapshotSchema('not json')).toEqual([]);
    expect(parseSnapshotSchema('')).toEqual([]);
  });

  it('maps EnumRef field options inline so the Select renders choices', () => {
    const withOptions = JSON.stringify([
      {
        id: 20001,
        key: 'priority',
        type: 'EnumRef',
        labelVi: 'Mức độ',
        labelEn: 'Priority',
        displayOrder: 1,
        required: true,
        options: [
          { id: 5, value: 'low', labelVi: 'Thấp', labelEn: 'Low', displayOrder: 1 },
          { id: 6, value: 'mid', labelVi: 'Trung bình', labelEn: 'Medium', displayOrder: 2 },
        ],
      },
    ]);

    const [field] = parseSnapshotSchema(withOptions);

    expect(field.options).toHaveLength(2);
    // id is string-normalized to match FormFieldOptionDto.
    expect(field.options?.[0]).toEqual({
      id: '5',
      value: 'low',
      labelVi: 'Thấp',
      labelEn: 'Low',
      displayOrder: 1,
    });
    expect(field.options?.[1].value).toBe('mid');
  });

  it('maps options to null for fields with no options key (backward-compat)', () => {
    // The Text field in SNAPSHOT_ENVELOPE carries no options key — older snapshots stay valid.
    const [fullName] = parseSnapshotSchema(SNAPSHOT_ENVELOPE);
    expect(fullName.options).toBeNull();
  });
});
