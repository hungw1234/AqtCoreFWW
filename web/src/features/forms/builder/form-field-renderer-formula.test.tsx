/**
 * form-field-renderer-formula.test.tsx — Formula field render contract.
 *
 * Locks: (1) builder preview (no computedValues) → disabled "(computed)" placeholder, no crash;
 * (2) with computedValues → disabled input shows the BE-supplied value as a string;
 * (3) missing key → blank; (4) the value is display-only (input is disabled, never editable).
 * No client-side expression evaluation exists here — the widget only mirrors what it is handed.
 */

import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Form } from 'antd';
import type { TFunction } from 'i18next';
import { renderInteractiveField } from './form-field-renderer';
import type { FormFieldDto } from '../form-types';

const t = ((key: string) => key) as unknown as TFunction;

function formulaField(): FormFieldDto {
  return {
    id: 'f1', fieldKey: 'total', type: 'Formula', labelVi: 'Tổng', labelEn: 'Total',
    required: false, displayOrder: 1, isActive: true, addedInVersion: 1,
  };
}

function renderFormula(computedValues?: Record<string, unknown>) {
  return render(
    <Form>{renderInteractiveField({ field: formulaField(), computedValues, t })}</Form>
  );
}

describe('renderInteractiveField — Formula case', () => {
  it('builder preview (no computedValues) renders a disabled placeholder, no crash', () => {
    renderFormula(undefined);
    const input = screen.getByPlaceholderText('(computed)') as HTMLInputElement;
    expect(input.disabled).toBe(true);
    expect(input.value).toBe('');
  });

  it('renders the BE-computed value as a string in a disabled input', () => {
    renderFormula({ total: 42 });
    const input = screen.getByPlaceholderText('(computed)') as HTMLInputElement;
    expect(input.disabled).toBe(true);
    expect(input.value).toBe('42');
  });

  it('coerces non-string computed values (boolean) to string', () => {
    renderFormula({ total: true });
    expect((screen.getByPlaceholderText('(computed)') as HTMLInputElement).value).toBe('true');
  });

  it('renders blank when the field key is absent from the results map (null-omitted key)', () => {
    renderFormula({ somethingElse: 1 });
    expect((screen.getByPlaceholderText('(computed)') as HTMLInputElement).value).toBe('');
  });
});
