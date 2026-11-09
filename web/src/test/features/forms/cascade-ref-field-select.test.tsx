// cascade-ref-field-select.test.tsx — P5 behavioral coverage for cascade in RefFieldSelect:
// (1) a cascade child fetches with its parent's value only once the parent is set,
// (2) a parent change refetches with the new value AND clears the child's stale value (clear-propagation),
// (3) a multi-parent child does not fetch until ALL parents are set.
//
// useFieldOptions is mocked to capture the (fieldId, parentValues, enabled) it is called with.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, act } from '@testing-library/react';
import { createElement, type ReactNode, useEffect } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Form, Input } from 'antd';
import type { FormInstance } from 'antd';

// Capture every useFieldOptions call's args.
const optionsCalls: Array<{ fieldId: string; parentValues?: Record<string, string>; enabled: boolean }> = [];
vi.mock('@/features/forms/form-api', () => ({
  useFieldOptions: (
    _templateId: string,
    fieldId: string,
    parentValues?: Record<string, string>,
    enabled = true,
  ) => {
    optionsCalls.push({ fieldId, parentValues, enabled });
    return { data: [{ value: 'w1', label: 'Ward 1' }], isLoading: false };
  },
  // usePublicFieldOptions is imported unconditionally by RefFieldSelect (added in cascade v1 P4 for the
  // anonymous public path). Even when publicCode is undefined the module resolution fires, so the mock
  // must export it or the test file crashes at rules-of-hooks eval time. Return a static shape here -
  // this test only exercises the authed path (publicCode=undefined ⇒ this hook's data is never read).
  usePublicFieldOptions: () => ({ data: [], isLoading: false }),
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (k: string) => k, i18n: { language: 'vi' } }),
}));

import { RefFieldSelect } from '@/features/forms/builder/ref-field-select';
import type { FormFieldDto } from '@/features/forms/form-types';

function makeChild(deps: Array<{ dependsOnFieldKey: string; filterParamName: string }>): FormFieldDto {
  return {
    id: 'fld-child',
    fieldKey: 'xa',
    type: 'ExternalRef',
    labelVi: 'Xã',
    labelEn: 'Ward',
    displayOrder: 2,
    required: false,
    dataSourceJson: JSON.stringify({ ApiUrl: 'https://x', dependencies: deps }),
  } as FormFieldDto;
}

function Wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client: qc }, children);
}

/**
 * Renders the cascade child inside a Form and exposes the form instance to the test.
 * The parent fields are rendered as REAL Form.Items — Form.useWatch only tracks registered fields,
 * which mirrors production where every parent is a rendered field (an unregistered setFieldValue is
 * invisible to useWatch by design).
 */
function renderChild(
  field: FormFieldDto,
  parentKeys: string[],
  onForm: (f: FormInstance) => void,
) {
  function Inner() {
    const [form] = Form.useForm();
    useEffect(() => {
      onForm(form);
    }, [form]);
    return (
      <Form form={form}>
        {parentKeys.map((k) => (
          <Form.Item key={k} name={k}>
            <Input />
          </Form.Item>
        ))}
        <Form.Item name="xa">
          <RefFieldSelect field={field} templateId="tpl-1" />
        </Form.Item>
      </Form>
    );
  }
  return render(<Inner />, { wrapper: Wrapper });
}

beforeEach(() => {
  optionsCalls.length = 0;
});

describe('RefFieldSelect cascade — P5', () => {
  it('single parent: does not fetch until the parent is set, then fetches with its value', async () => {
    let form!: FormInstance;
    renderChild(
      makeChild([{ dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' }]),
      ['tinh'],
      (f) => (form = f),
    );

    // Initially the parent is unset → the fetch is disabled (no unfiltered leak).
    expect(optionsCalls.at(-1)?.enabled).toBe(false);

    // Set the parent → the child should now fetch with { provinceCode: HN } enabled.
    await act(async () => {
      form.setFieldValue('tinh', 'HN');
    });

    const last = optionsCalls.at(-1)!;
    expect(last.enabled).toBe(true);
    expect(last.parentValues).toEqual({ provinceCode: 'HN' });
  });

  it('parent change clears the stale child value (clear-propagation)', async () => {
    let form!: FormInstance;
    renderChild(
      makeChild([{ dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' }]),
      ['tinh'],
      (f) => (form = f),
    );

    await act(async () => {
      form.setFieldValue('tinh', 'HN');
      form.setFieldValue('xa', 'w1'); // child picks a ward of HN
    });

    // Switch province → the child's stale value must be cleared.
    await act(async () => {
      form.setFieldValue('tinh', 'HCM');
    });

    expect(form.getFieldValue('xa')).toBeUndefined();
    expect(optionsCalls.at(-1)?.parentValues).toEqual({ provinceCode: 'HCM' });
  });

  it('multi-parent: no fetch until ALL parents are set', async () => {
    let form!: FormInstance;
    renderChild(
      makeChild([
        { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
        { dependsOnFieldKey: 'huyen', filterParamName: 'districtCode' },
      ]),
      ['tinh', 'huyen'],
      (f) => (form = f),
    );

    await act(async () => {
      form.setFieldValue('tinh', 'HN'); // only one of two parents
    });
    expect(optionsCalls.at(-1)?.enabled).toBe(false);

    await act(async () => {
      form.setFieldValue('huyen', 'Q1'); // now both set
    });
    const last = optionsCalls.at(-1)!;
    expect(last.enabled).toBe(true);
    expect(last.parentValues).toEqual({ provinceCode: 'HN', districtCode: 'Q1' });
  });
});
