// Tests for form-step-utils.ts — splitIntoSteps, buildValidationRules, buildRequiredIfRules
import { describe, it, expect } from 'vitest';
import { splitIntoSteps, buildValidationRules, buildRequiredIfRules } from '../form-step-utils';
import type { FormFieldDto } from '../form-types';

// Helper to create minimal FormFieldDto
function makeField(overrides: Partial<FormFieldDto>): FormFieldDto {
  return {
    id: 'f1',
    fieldKey: 'field1',
    type: 'Text',
    labelVi: 'Field 1',
    labelEn: 'Field 1',
    required: false,
    displayOrder: 1,
    isActive: true,
    addedInVersion: 1,
    ...overrides,
  };
}

const t = (key: string) => key;

describe('splitIntoSteps', () => {
  it('returns single General step when no Sections', () => {
    const fields = [makeField({ type: 'Text' }), makeField({ type: 'Number', id: 'f2' })];
    const steps = splitIntoSteps(fields);
    expect(steps).toHaveLength(1);
    expect(steps[0].title).toBe('General');
    expect(steps[0].fields).toHaveLength(2);
  });

  it('splits on Section fields', () => {
    const fields = [
      makeField({ type: 'Text', id: 'f1' }),
      makeField({ type: 'Section', id: 'sec1', labelVi: 'Step 2' }),
      makeField({ type: 'Number', id: 'f2' }),
    ];
    const steps = splitIntoSteps(fields);
    expect(steps).toHaveLength(2);
    expect(steps[0].title).toBe('General');
    expect(steps[1].title).toBe('Step 2');
  });
});

describe('buildValidationRules', () => {
  it('returns empty for no validationRulesJson', () => {
    expect(buildValidationRules(makeField({}), t)).toEqual([]);
  });

  it('returns pattern rule', () => {
    const field = makeField({ validationRulesJson: JSON.stringify({ pattern: '^[A-Z]+$' }) });
    const rules = buildValidationRules(field, t);
    expect(rules).toHaveLength(1);
    expect(rules[0]).toHaveProperty('pattern');
  });

  it('returns minLength/maxLength rules', () => {
    const field = makeField({ validationRulesJson: JSON.stringify({ minLength: 5, maxLength: 100 }) });
    const rules = buildValidationRules(field, t);
    expect(rules).toHaveLength(2);
  });

  it('returns min/max validator', () => {
    const field = makeField({ validationRulesJson: JSON.stringify({ min: 1, max: 10 }) });
    const rules = buildValidationRules(field, t);
    expect(rules).toHaveLength(1);
    expect(rules[0]).toHaveProperty('validator');
  });

  it('handles invalid JSON gracefully', () => {
    const field = makeField({ validationRulesJson: 'not-json' });
    expect(buildValidationRules(field, t)).toEqual([]);
  });
});

describe('buildRequiredIfRules', () => {
  it('returns empty when no requiredIfJson', () => {
    expect(buildRequiredIfRules(makeField({}), {}, t)).toEqual([]);
  });

  it('returns empty when conditions not met', () => {
    const field = makeField({
      requiredIfJson: JSON.stringify([{ fieldKey: 'leaveType', operator: 'eq', value: 'sick' }]),
    });
    const rules = buildRequiredIfRules(field, { leaveType: 'annual' }, t);
    expect(rules).toEqual([]);
  });

  it('returns required rule when conditions met (AND logic)', () => {
    const field = makeField({
      requiredIfJson: JSON.stringify([
        { fieldKey: 'leaveType', operator: 'eq', value: 'sick' },
        { fieldKey: 'days', operator: 'gt', value: '3' },
      ]),
    });
    const rules = buildRequiredIfRules(field, { leaveType: 'sick', days: '5' }, t);
    expect(rules).toHaveLength(1);
    expect(rules[0]).toHaveProperty('required', true);
  });

  it('returns empty when only some conditions met (AND requires all)', () => {
    const field = makeField({
      requiredIfJson: JSON.stringify([
        { fieldKey: 'leaveType', operator: 'eq', value: 'sick' },
        { fieldKey: 'days', operator: 'gt', value: '3' },
      ]),
    });
    const rules = buildRequiredIfRules(field, { leaveType: 'sick', days: '2' }, t);
    expect(rules).toEqual([]);
  });

  it('supports gte/lte operators via evalCondition', () => {
    const field = makeField({
      requiredIfJson: JSON.stringify([{ fieldKey: 'score', operator: 'gte', value: '50' }]),
    });
    expect(buildRequiredIfRules(field, { score: '50' }, t)).toHaveLength(1);
    expect(buildRequiredIfRules(field, { score: '49' }, t)).toEqual([]);
  });

  it('handles invalid JSON gracefully', () => {
    const field = makeField({ requiredIfJson: 'bad-json' });
    expect(buildRequiredIfRules(field, {}, t)).toEqual([]);
  });

  it('handles empty conditions array', () => {
    const field = makeField({ requiredIfJson: '[]' });
    expect(buildRequiredIfRules(field, {}, t)).toEqual([]);
  });
});
