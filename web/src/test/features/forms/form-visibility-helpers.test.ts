// form-visibility-helpers.test.ts — tests for visibility rule evaluation
// Pure function tests for isFieldVisible

import { describe, it, expect } from 'vitest';
import { isFieldVisible, parseVisibilityRules } from '@/features/forms/form-visibility-helpers';

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-001: isFieldVisible — no rules
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-001', () => {
  it('returns true when no visibilityRulesJson defined', () => {
    const result = isFieldVisible(undefined, { fieldKey: 'value' });
    expect(result).toBe(true);
  });

  it('returns true when visibilityRulesJson is null', () => {
    const result = isFieldVisible(null, { fieldKey: 'value' });
    expect(result).toBe(true);
  });

  it('returns true when visibilityRulesJson is empty string', () => {
    const result = isFieldVisible('', { fieldKey: 'value' });
    expect(result).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-002: isFieldVisible — empty conditions array
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-002', () => {
  it('returns true when conditions array is empty', () => {
    const rules = JSON.stringify({ match: 'all' as const, conditions: [] });
    const result = isFieldVisible(rules, { fieldKey: 'value' });
    expect(result).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-003: isFieldVisible — single eq condition
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-003', () => {
  it('returns true when eq condition matches', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'status', operator: 'eq' as const, value: 'active' }],
    });
    const result = isFieldVisible(rules, { status: 'active' });
    expect(result).toBe(true);
  });

  it('returns false when eq condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'status', operator: 'eq' as const, value: 'active' }],
    });
    const result = isFieldVisible(rules, { status: 'inactive' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-004: isFieldVisible — single neq condition
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-004', () => {
  it('returns true when neq condition matches', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'status', operator: 'neq' as const, value: 'draft' }],
    });
    const result = isFieldVisible(rules, { status: 'active' });
    expect(result).toBe(true);
  });

  it('returns false when neq condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'status', operator: 'neq' as const, value: 'active' }],
    });
    const result = isFieldVisible(rules, { status: 'active' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-005: isFieldVisible — numeric gt/lt conditions
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-005', () => {
  it('returns true when gt condition matches', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'age', operator: 'gt' as const, value: '18' }],
    });
    const result = isFieldVisible(rules, { age: '21' });
    expect(result).toBe(true);
  });

  it('returns false when gt condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'age', operator: 'gt' as const, value: '25' }],
    });
    const result = isFieldVisible(rules, { age: '20' });
    expect(result).toBe(false);
  });

  it('returns true when lt condition matches', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'age', operator: 'lt' as const, value: '65' }],
    });
    const result = isFieldVisible(rules, { age: '30' });
    expect(result).toBe(true);
  });

  it('returns false when lt condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'age', operator: 'lt' as const, value: '25' }],
    });
    const result = isFieldVisible(rules, { age: '30' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-006: isFieldVisible — contains condition
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-006', () => {
  it('returns true when contains condition matches', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'email', operator: 'contains' as const, value: '@example.com' }],
    });
    const result = isFieldVisible(rules, { email: 'user@example.com' });
    expect(result).toBe(true);
  });

  it('returns false when contains condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'email', operator: 'contains' as const, value: '@company.com' }],
    });
    const result = isFieldVisible(rules, { email: 'user@example.com' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-007: isFieldVisible — empty/notEmpty conditions
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-007', () => {
  it('returns true when empty condition matches (value is empty string)', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'optional', operator: 'empty' as const }],
    });
    const result = isFieldVisible(rules, { optional: '' });
    expect(result).toBe(true);
  });

  it('returns true when empty condition matches (value is null)', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'optional', operator: 'empty' as const }],
    });
    const result = isFieldVisible(rules, { optional: null });
    expect(result).toBe(true);
  });

  it('returns false when empty condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'optional', operator: 'empty' as const }],
    });
    const result = isFieldVisible(rules, { optional: 'has value' });
    expect(result).toBe(false);
  });

  it('returns true when notEmpty condition matches', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'required', operator: 'notEmpty' as const }],
    });
    const result = isFieldVisible(rules, { required: 'has value' });
    expect(result).toBe(true);
  });

  it('returns false when notEmpty condition does not match', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'required', operator: 'notEmpty' as const }],
    });
    const result = isFieldVisible(rules, { required: '' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-008: isFieldVisible — match='all' (AND logic)
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-008', () => {
  it('returns true when ALL conditions match (AND logic)', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [
        { fieldKey: 'type', operator: 'eq' as const, value: 'business' },
        { fieldKey: 'status', operator: 'neq' as const, value: 'rejected' },
      ],
    });
    const result = isFieldVisible(rules, { type: 'business', status: 'active' });
    expect(result).toBe(true);
  });

  it('returns false when one condition fails (AND logic)', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [
        { fieldKey: 'type', operator: 'eq' as const, value: 'business' },
        { fieldKey: 'status', operator: 'eq' as const, value: 'active' },
      ],
    });
    const result = isFieldVisible(rules, { type: 'business', status: 'inactive' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-009: isFieldVisible — match='any' (OR logic)
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-009', () => {
  it('returns true when ONE condition matches (OR logic)', () => {
    const rules = JSON.stringify({
      match: 'any' as const,
      conditions: [
        { fieldKey: 'type', operator: 'eq' as const, value: 'business' },
        { fieldKey: 'type', operator: 'eq' as const, value: 'personal' },
      ],
    });
    const result = isFieldVisible(rules, { type: 'personal' });
    expect(result).toBe(true);
  });

  it('returns false when NO conditions match (OR logic)', () => {
    const rules = JSON.stringify({
      match: 'any' as const,
      conditions: [
        { fieldKey: 'status', operator: 'eq' as const, value: 'approved' },
        { fieldKey: 'status', operator: 'eq' as const, value: 'pending' },
      ],
    });
    const result = isFieldVisible(rules, { status: 'rejected' });
    expect(result).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-010: isFieldVisible — invalid JSON
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-010', () => {
  it('returns true (safe fallback) when JSON is invalid', () => {
    const result = isFieldVisible('invalid json {]', { fieldKey: 'value' });
    expect(result).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-011: isFieldVisible — null/undefined field values
// ──────────────────────────────────────────────────────────────────────────────

describe('isFieldVisible — TC-FE-VIS-011', () => {
  it('handles undefined field value correctly', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'missing', operator: 'empty' as const }],
    });
    const result = isFieldVisible(rules, {});
    expect(result).toBe(true);
  });

  it('converts numeric values to string for comparison', () => {
    const rules = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'count', operator: 'eq' as const, value: '5' }],
    });
    const result = isFieldVisible(rules, { count: 5 });
    expect(result).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIS-012: parseVisibilityRules — invalid JSON
// ──────────────────────────────────────────────────────────────────────────────

describe('parseVisibilityRules — TC-FE-VIS-012', () => {
  it('returns null when JSON is invalid', () => {
    const result = parseVisibilityRules('invalid json');
    expect(result).toBeNull();
  });

  it('returns null when input is undefined', () => {
    const result = parseVisibilityRules(undefined);
    expect(result).toBeNull();
  });

  it('returns null when input is null', () => {
    const result = parseVisibilityRules(null);
    expect(result).toBeNull();
  });

  it('returns parsed rules when JSON is valid', () => {
    const json = JSON.stringify({
      match: 'all' as const,
      conditions: [{ fieldKey: 'test', operator: 'eq' as const, value: 'value' }],
    });
    const result = parseVisibilityRules(json);
    expect(result).not.toBeNull();
    expect(result?.conditions).toHaveLength(1);
  });
});
