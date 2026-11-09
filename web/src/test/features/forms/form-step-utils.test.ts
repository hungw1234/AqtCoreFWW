// form-step-utils.test.ts — tests for step splitting and validation rule building
// Pure function tests — NO mocking needed, highest value tests.

import { describe, it, expect } from 'vitest';
import { splitIntoSteps, buildValidationRules } from '@/features/forms/form-step-utils';
import type { FormFieldDto } from '@/features/forms/form-types';

// ──────────────────────────────────────────────────────────────────────────────
// Helper: create mock field
// ──────────────────────────────────────────────────────────────────────────────

function makeField(overrides: Partial<FormFieldDto> = {}): FormFieldDto {
  return {
    id: 'f1',
    fieldKey: 'test_field',
    type: 'Text',
    labelVi: 'Test Label',
    labelEn: 'Test Label',
    required: false,
    displayOrder: 1,
    isActive: true,
    addedInVersion: 1,
    ...overrides,
  };
}

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-STEP-001: splitIntoSteps — empty fields
// ──────────────────────────────────────────────────────────────────────────────

describe('splitIntoSteps — TC-FE-STEP-001', () => {
  it('returns one empty "General" step for empty fields array', () => {
    const result = splitIntoSteps([]);
    expect(result).toHaveLength(1);
    expect(result[0].title).toBe('General');
    expect(result[0].fields).toHaveLength(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-STEP-002: splitIntoSteps — no Section fields
// ──────────────────────────────────────────────────────────────────────────────

describe('splitIntoSteps — TC-FE-STEP-002', () => {
  it('puts all fields without Section into single "General" step', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', displayOrder: 1 }),
      makeField({ id: 'f2', fieldKey: 'email', displayOrder: 2 }),
      makeField({ id: 'f3', fieldKey: 'age', displayOrder: 3 }),
    ];
    const result = splitIntoSteps(fields);
    expect(result).toHaveLength(1);
    expect(result[0].title).toBe('General');
    expect(result[0].fields).toHaveLength(3);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-STEP-003: splitIntoSteps — single Section
// ──────────────────────────────────────────────────────────────────────────────

describe('splitIntoSteps — TC-FE-STEP-003', () => {
  it('creates General step + named step when one Section present', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', displayOrder: 1 }),
      makeField({ id: 'f2', fieldKey: 'section1', type: 'Section', labelVi: 'Part 2', displayOrder: 2 }),
      makeField({ id: 'f3', fieldKey: 'email', displayOrder: 3 }),
    ];
    const result = splitIntoSteps(fields);
    expect(result).toHaveLength(2);
    expect(result[0].title).toBe('General');
    expect(result[0].fields).toHaveLength(1);
    expect(result[1].title).toBe('Part 2');
    expect(result[1].fields).toHaveLength(1);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-STEP-004: splitIntoSteps — multiple Sections
// ──────────────────────────────────────────────────────────────────────────────

describe('splitIntoSteps — TC-FE-STEP-004', () => {
  it('creates step for each Section field with correct field assignment', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', displayOrder: 1 }),
      makeField({ id: 'f2', fieldKey: 'section1', type: 'Section', labelVi: 'Part A', displayOrder: 2 }),
      makeField({ id: 'f3', fieldKey: 'email', displayOrder: 3 }),
      makeField({ id: 'f4', fieldKey: 'section2', type: 'Section', labelVi: 'Part B', displayOrder: 4 }),
      makeField({ id: 'f5', fieldKey: 'phone', displayOrder: 5 }),
    ];
    const result = splitIntoSteps(fields);
    expect(result).toHaveLength(3);
    expect(result[0].title).toBe('General');
    expect(result[0].fields.map((f) => f.fieldKey)).toEqual(['name']);
    expect(result[1].title).toBe('Part A');
    expect(result[1].fields.map((f) => f.fieldKey)).toEqual(['email']);
    expect(result[2].title).toBe('Part B');
    expect(result[2].fields.map((f) => f.fieldKey)).toEqual(['phone']);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-STEP-005: splitIntoSteps — Section at beginning (no empty General)
// ──────────────────────────────────────────────────────────────────────────────

describe('splitIntoSteps — TC-FE-STEP-005', () => {
  it('does not create empty General step when Section is first field', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'section1', type: 'Section', labelVi: 'Contact Info', displayOrder: 1 }),
      makeField({ id: 'f2', fieldKey: 'email', displayOrder: 2 }),
    ];
    const result = splitIntoSteps(fields);
    expect(result).toHaveLength(1);
    expect(result[0].title).toBe('Contact Info');
    expect(result[0].fields).toHaveLength(1);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-STEP-006: splitIntoSteps — consecutive Sections (no content between)
// ──────────────────────────────────────────────────────────────────────────────

describe('splitIntoSteps — TC-FE-STEP-006', () => {
  it('handles consecutive Section fields without content between them', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', displayOrder: 1 }),
      makeField({ id: 'f2', fieldKey: 'section1', type: 'Section', labelVi: 'Part A', displayOrder: 2 }),
      makeField({ id: 'f3', fieldKey: 'section2', type: 'Section', labelVi: 'Part B', displayOrder: 3 }),
      makeField({ id: 'f4', fieldKey: 'email', displayOrder: 4 }),
    ];
    const result = splitIntoSteps(fields);
    // Should have: General (name), empty Part A should be skipped, Part B (email)
    expect(result.length).toBeGreaterThanOrEqual(2);
    expect(result.some((s) => s.title === 'Part A')).toBe(false); // Empty section not added
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-001: buildValidationRules — no validationRulesJson
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-001', () => {
  it('returns empty array when no validationRulesJson defined', () => {
    const field = makeField({ validationRulesJson: undefined });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules).toHaveLength(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-002: buildValidationRules — pattern rule
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-002', () => {
  it('returns pattern Rule when pattern defined and valid', () => {
    const field = makeField({
      validationRulesJson: JSON.stringify({ pattern: '^[A-Z]+$' }),
    });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules).toHaveLength(1);
    expect(rules[0]).toHaveProperty('pattern');
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-003: buildValidationRules — minLength/maxLength rules
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-003', () => {
  it('returns min/max Rules for minLength/maxLength', () => {
    const field = makeField({
      validationRulesJson: JSON.stringify({ minLength: 5, maxLength: 50 }),
    });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules).toHaveLength(2);
    expect(rules.some((r) => (r as any).min === 5)).toBe(true);
    expect(rules.some((r) => (r as any).max === 50)).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-004: buildValidationRules — numeric min/max
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-004', () => {
  it('returns validator Rule for numeric min/max bounds', () => {
    const field = makeField({
      validationRulesJson: JSON.stringify({ min: 10, max: 100 }),
    });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules).toHaveLength(1);
    expect(rules[0]).toHaveProperty('validator');
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-005: buildValidationRules — invalid JSON
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-005', () => {
  it('returns empty array when JSON is invalid (no crash)', () => {
    const field = makeField({
      validationRulesJson: 'not valid json {]',
    });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules).toHaveLength(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-006: buildValidationRules — pattern > 200 chars (ReDoS guard)
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-006', () => {
  it('skips pattern rule when pattern > 200 chars (ReDoS protection)', () => {
    const longPattern = 'a'.repeat(201);
    const field = makeField({
      validationRulesJson: JSON.stringify({ pattern: longPattern }),
    });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules).toHaveLength(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-007: buildValidationRules — invalid regex pattern
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-007', () => {
  it('skips invalid regex pattern (try-catch guard, no crash)', () => {
    const field = makeField({
      validationRulesJson: JSON.stringify({ pattern: '(?P<invalid>' }), // Invalid regex
    });
    const rules = buildValidationRules(field, (k) => k);
    // Should catch error and skip pattern rule
    expect(rules).toHaveLength(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VALID-008: buildValidationRules — combined rules
// ──────────────────────────────────────────────────────────────────────────────

describe('buildValidationRules — TC-FE-VALID-008', () => {
  it('returns all applicable rules when multiple defined', () => {
    const field = makeField({
      validationRulesJson: JSON.stringify({
        pattern: '^[a-z]+$',
        minLength: 3,
        maxLength: 20,
      }),
    });
    const rules = buildValidationRules(field, (k) => k);
    expect(rules.length).toBeGreaterThanOrEqual(3);
  });
});
