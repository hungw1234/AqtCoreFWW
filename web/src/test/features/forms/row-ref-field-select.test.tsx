// row-ref-field-select.test.tsx — M1 Phase-07 (2026-08-12) in-row cascade wiring.
//
// Verifies the row-scoped variant of the RefFieldSelect cascade contract:
//   1. buildParentValues sources from the `rowValues` prop (NOT antd Form.useWatch), so plain-state
//      rows inside a TableField widget are visible to the cascade.
//   2. Non-cascade path (child with `dependencies:[]` empty) fetches options with parentValues=undefined
//      and does NOT clear on unrelated row updates - backward-safe.
//   3. Clear-propagation fires only when the row's parent-column changes AFTER the first settled watch
//      (edit/draft-resume with pre-loaded parent+child does not wipe the loaded child value).

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode, useState } from 'react';
import type { FormFieldDto } from '@/features/forms/form-types';

// ──────────────────────────────────────────────────────────────────────────────
// Spies: capture the calls to the option-fetch hooks so we can assert `parentValues`.
// ──────────────────────────────────────────────────────────────────────────────

const useFieldOptionsSpy = vi.fn((..._args: unknown[]) => ({ data: [], isLoading: false }));
const usePublicFieldOptionsSpy = vi.fn((..._args: unknown[]) => ({ data: [], isLoading: false }));

vi.mock('@/features/forms/form-api', () => ({
  useFieldOptions: (...args: unknown[]) => useFieldOptionsSpy(...args),
  usePublicFieldOptions: (...args: unknown[]) => usePublicFieldOptionsSpy(...args),
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Component-under-test — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { RowRefFieldSelect } from '@/features/forms/builder/row-ref-field-select';

function withQuery(node: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client: qc }, node);
}

function makeChildField(overrides: Partial<FormFieldDto> = {}): FormFieldDto {
  return {
    id: 42,
    fieldKey: 'city',
    type: 'EnumRef',
    labelVi: 'Xã/Phường',
    label: 'City',
    required: false,
    displayOrder: 1,
    isActive: true,
    dataSourceJson:
      '{"dependencies":[{"dependsOnFieldKey":"province","filterParamName":"provinceCode"}]}',
    ...overrides,
  } as unknown as FormFieldDto;
}

beforeEach(() => {
  useFieldOptionsSpy.mockClear();
  usePublicFieldOptionsSpy.mockClear();
});

describe('RowRefFieldSelect - row-scoped cascade wiring (M1 Phase-07)', () => {
  it('passes parentValues built from rowValues to useFieldOptions', () => {
    const child = makeChildField();
    render(
      withQuery(
        <RowRefFieldSelect
          field={child}
          templateId="tpl-1"
          rowValues={{ province: 'HN' }}
        />,
      ),
    );

    // Cascade path is enabled (all parents set) and passes the row's HN value keyed by filterParamName.
    expect(useFieldOptionsSpy).toHaveBeenCalled();
    const call = useFieldOptionsSpy.mock.calls[useFieldOptionsSpy.mock.calls.length - 1];
    expect(call[0]).toBe('tpl-1'); // templateId
    expect(call[1]).toBe(42); // fieldId
    expect(call[2]).toEqual({ provinceCode: 'HN' }); // parentValues from rowValues
    expect(call[3]).toBe(true); // enabled (allParentsSet && !isPublic)
  });

  it('routes through usePublicFieldOptions when publicCode is set', () => {
    const child = makeChildField();
    render(
      withQuery(
        <RowRefFieldSelect
          field={child}
          templateId=""
          publicCode="MY-FORM-CODE"
          rowValues={{ province: 'HCM' }}
        />,
      ),
    );

    expect(usePublicFieldOptionsSpy).toHaveBeenCalled();
    const call =
      usePublicFieldOptionsSpy.mock.calls[usePublicFieldOptionsSpy.mock.calls.length - 1];
    expect(call[0]).toBe('MY-FORM-CODE');
    expect(call[1]).toBe('city');
    expect(call[2]).toEqual({ provinceCode: 'HCM' });
    expect(call[3]).toBe(true); // enabled (public path)
  });

  it('gates the fetch (enabled=false) when a required parent is missing from rowValues', () => {
    const child = makeChildField();
    render(
      withQuery(
        <RowRefFieldSelect
          field={child}
          templateId="tpl-1"
          rowValues={{}} // parent absent
        />,
      ),
    );

    const call = useFieldOptionsSpy.mock.calls[useFieldOptionsSpy.mock.calls.length - 1];
    expect(call[3]).toBe(false); // !allParentsSet ⇒ enabled=false, avoids unfiltered flash
  });

  it('backward path: non-cascade child (empty dependencies) fetches with parentValues=undefined and stays enabled', () => {
    const child = makeChildField({ dataSourceJson: null } as Partial<FormFieldDto>);
    render(
      withQuery(
        <RowRefFieldSelect
          field={child}
          templateId="tpl-1"
          rowValues={{}}
        />,
      ),
    );

    const call = useFieldOptionsSpy.mock.calls[useFieldOptionsSpy.mock.calls.length - 1];
    expect(call[2]).toBeUndefined(); // no cascade ⇒ no parent params
    expect(call[3]).toBe(true); // !hasDeps ⇒ enabled regardless
  });

  it('does NOT clear the pre-loaded child value on the first settled render (edit/draft-resume guard)', () => {
    const child = makeChildField();
    const onChange = vi.fn();
    render(
      withQuery(
        <RowRefFieldSelect
          field={child}
          templateId="tpl-1"
          rowValues={{ province: 'HN' }}
          value="01001"
          onChange={onChange}
        />,
      ),
    );

    // First-watch guard: seed signature without emitting a clear. Editing a draft with saved parent+
    // child must NOT wipe the loaded child value on the initial render.
    expect(onChange).not.toHaveBeenCalled();
  });

  it('clears child value when the row parent changes AFTER the first settled render', () => {
    const child = makeChildField();
    const onChange = vi.fn();

    // Harness: real state so a props-update triggers a real signature change post-first-render.
    function Harness() {
      const [rv, setRv] = useState<Record<string, unknown>>({ province: 'HN' });
      return (
        <>
          <button data-testid="switch-province" onClick={() => setRv({ province: 'HCM' })}>
            switch
          </button>
          <RowRefFieldSelect
            field={child}
            templateId="tpl-1"
            rowValues={rv}
            value="01001"
            onChange={onChange}
          />
        </>
      );
    }

    const { getByTestId } = render(withQuery(<Harness />));
    // Simulate the user changing the row's province from HN to HCM.
    // Wrap in act() so React 19 flushes both the state update AND the useEffect that reads the new
    // parentSignature. Without act(), the assertion below runs before the effect fires.
    act(() => {
      getByTestId('switch-province').click();
    });

    // Genuine parent change AFTER the seeded first watch ⇒ clear-on-change fires with undefined.
    expect(onChange).toHaveBeenCalledWith(undefined);
  });
});
