// form-step-utils.ts — shared step-splitting and validation-rule utilities
// Used by both form-preview-modal (builder) and public-form-page

import type { FormFieldDto, VisibilityCondition } from './form-types';
import type { Rule } from 'antd/es/form';
import { evalCondition } from './form-visibility-helpers';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface FormStep {
  title: string;
  fields: FormFieldDto[];
}

// ---------------------------------------------------------------------------
// Step splitting logic — each Section field starts a new wizard step
// ---------------------------------------------------------------------------

/**
 * Split sorted fields into wizard steps.
 * A Section field starts a new named step; fields before the first Section
 * go into an implicit "General" step.
 */
export function splitIntoSteps(fields: FormFieldDto[]): FormStep[] {
  const steps: FormStep[] = [];
  let current: FormStep = { title: 'General', fields: [] };

  for (const field of fields) {
    if (field.type === 'Section') {
      // Section itself becomes the step title; push previous step if it has content
      if (current.fields.length > 0) steps.push(current);
      current = { title: field.labelVi || field.fieldKey || 'Step', fields: [] };
    } else {
      current.fields.push(field);
    }
  }
  if (current.fields.length > 0 || steps.length === 0) steps.push(current);
  return steps;
}

// ---------------------------------------------------------------------------
// Client-side validation rule builder — parses validationRulesJson
// ---------------------------------------------------------------------------

/**
 * Build Ant Design Form rules from a field's validationRulesJson.
 * Supports: pattern, minLength, maxLength, min, max.
 * Returns empty array if no rules defined or JSON is invalid.
 */
export function buildValidationRules(field: FormFieldDto, t: (key: string) => string): Rule[] {
  const rules: Rule[] = [];
  if (!field.validationRulesJson) return rules;
  try {
    const vr = JSON.parse(field.validationRulesJson) as {
      pattern?: string;
      minLength?: number;
      maxLength?: number;
      min?: number;
      max?: number;
    };

    if (vr.pattern && vr.pattern.length <= 200) {
      try {
        rules.push({ pattern: new RegExp(vr.pattern), message: t('validation.patternInvalid') });
      } catch { /* invalid regex — skip */ }
    }
    if (vr.minLength != null) {
      rules.push({ min: vr.minLength, message: `${t('validation.minLength')}: ${vr.minLength}` });
    }
    if (vr.maxLength != null) {
      rules.push({ max: vr.maxLength, message: `${t('validation.maxLength')}: ${vr.maxLength}` });
    }
    if (vr.min != null || vr.max != null) {
      const min = vr.min;
      const max = vr.max;
      rules.push({
        validator: (_, value) => {
          const num = Number(value);
          if (min != null && num < min) return Promise.reject(`Min: ${min}`);
          if (max != null && num > max) return Promise.reject(`Max: ${max}`);
          return Promise.resolve();
        },
      });
    }
  } catch {
    // Invalid JSON — skip validation rules silently
  }
  return rules;
}

// ---------------------------------------------------------------------------
// RequiredIf rule builder — evaluates requiredIfJson conditions at runtime
// ---------------------------------------------------------------------------

/**
 * Build a dynamic required rule from a field's requiredIfJson.
 * Conditions use the same schema as visibility rules (AND logic — all must match).
 * Returns a required rule when conditions are met, empty array otherwise.
 */
export function buildRequiredIfRules(
  field: FormFieldDto,
  formValues: Record<string, unknown>,
  t: (key: string, opts?: Record<string, unknown>) => string
): Rule[] {
  if (!field.requiredIfJson) return [];
  try {
    const conditions = JSON.parse(field.requiredIfJson) as VisibilityCondition[];
    if (!conditions.length) return [];
    // All conditions must be true for field to become required (AND logic)
    const isRequired = conditions.every((c) => evalCondition(c, formValues));
    if (!isRequired) return [];
    const label = field.labelVi || field.fieldKey;
    return [{ required: true, message: t('validation.requiredIf', { defaultValue: `${label} is required` }) }];
  } catch {
    return [];
  }
}
