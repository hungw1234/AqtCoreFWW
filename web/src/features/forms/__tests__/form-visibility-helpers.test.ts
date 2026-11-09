// Tests for form-visibility-helpers.ts — evalCondition + isFieldVisible
import { describe, it, expect } from 'vitest';
import { evalCondition, parseVisibilityRules, isFieldVisible } from '../form-visibility-helpers';

describe('evalCondition', () => {
  const values = { age: '25', name: 'John', city: 'Hanoi', empty: '' };

  it('eq — string match', () => {
    expect(evalCondition({ fieldKey: 'name', operator: 'eq', value: 'John' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'name', operator: 'eq', value: 'Jane' }, values)).toBe(false);
  });

  it('neq — string mismatch', () => {
    expect(evalCondition({ fieldKey: 'name', operator: 'neq', value: 'Jane' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'name', operator: 'neq', value: 'John' }, values)).toBe(false);
  });

  it('gt — numeric greater than', () => {
    expect(evalCondition({ fieldKey: 'age', operator: 'gt', value: '20' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'age', operator: 'gt', value: '30' }, values)).toBe(false);
  });

  it('lt — numeric less than', () => {
    expect(evalCondition({ fieldKey: 'age', operator: 'lt', value: '30' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'age', operator: 'lt', value: '20' }, values)).toBe(false);
  });

  it('gte — numeric greater or equal', () => {
    expect(evalCondition({ fieldKey: 'age', operator: 'gte', value: '25' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'age', operator: 'gte', value: '26' }, values)).toBe(false);
  });

  it('lte — numeric less or equal', () => {
    expect(evalCondition({ fieldKey: 'age', operator: 'lte', value: '25' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'age', operator: 'lte', value: '24' }, values)).toBe(false);
  });

  it('contains — substring match', () => {
    expect(evalCondition({ fieldKey: 'city', operator: 'contains', value: 'Han' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'city', operator: 'contains', value: 'Sai' }, values)).toBe(false);
  });

  it('empty — field is empty or missing', () => {
    expect(evalCondition({ fieldKey: 'empty', operator: 'empty' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'missing', operator: 'empty' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'name', operator: 'empty' }, values)).toBe(false);
  });

  it('notEmpty — field has value', () => {
    expect(evalCondition({ fieldKey: 'name', operator: 'notEmpty' }, values)).toBe(true);
    expect(evalCondition({ fieldKey: 'empty', operator: 'notEmpty' }, values)).toBe(false);
  });

  it('unknown operator — defaults to true', () => {
    expect(evalCondition({ fieldKey: 'name', operator: 'unknown' as never }, values)).toBe(true);
  });
});

describe('parseVisibilityRules', () => {
  it('returns null for empty/null input', () => {
    expect(parseVisibilityRules(null)).toBeNull();
    expect(parseVisibilityRules(undefined)).toBeNull();
    expect(parseVisibilityRules('')).toBeNull();
  });

  it('parses valid JSON', () => {
    const json = JSON.stringify({ match: 'all', conditions: [{ fieldKey: 'x', operator: 'eq', value: '1' }] });
    const result = parseVisibilityRules(json);
    expect(result?.match).toBe('all');
    expect(result?.conditions).toHaveLength(1);
  });

  it('returns null for invalid JSON', () => {
    expect(parseVisibilityRules('not json')).toBeNull();
  });
});

describe('isFieldVisible', () => {
  it('returns true when no rules defined', () => {
    expect(isFieldVisible(null, {})).toBe(true);
    expect(isFieldVisible(undefined, {})).toBe(true);
  });

  it('returns true when all conditions met (match=all)', () => {
    const rules = JSON.stringify({
      match: 'all',
      conditions: [
        { fieldKey: 'a', operator: 'eq', value: '1' },
        { fieldKey: 'b', operator: 'eq', value: '2' },
      ],
    });
    expect(isFieldVisible(rules, { a: '1', b: '2' })).toBe(true);
    expect(isFieldVisible(rules, { a: '1', b: '3' })).toBe(false);
  });

  it('returns true when any condition met (match=any)', () => {
    const rules = JSON.stringify({
      match: 'any',
      conditions: [
        { fieldKey: 'a', operator: 'eq', value: '1' },
        { fieldKey: 'b', operator: 'eq', value: '2' },
      ],
    });
    expect(isFieldVisible(rules, { a: '1', b: '3' })).toBe(true);
    expect(isFieldVisible(rules, { a: '3', b: '3' })).toBe(false);
  });
});
