/**
 * ref-field-select-public-cascade.test.tsx — C1 slice: a reference field on a PUBLIC (anonymous) form
 * resolves its options through the public {code}/{fieldKey} path, never the authed template path.
 *
 * Locks:
 * (1) ROUTE — with publicCode set, RefFieldSelect drives usePublicFieldOptions(code, fieldKey, …) with
 *     enabled=true and leaves useFieldOptions disabled → an anonymous visitor never hits the Bearer
 *     endpoint (which returns 401 today, the functional gap this slice closes).
 * (2) REGRESSION — with NO publicCode (authed builder), the authed useFieldOptions is enabled and the
 *     public hook is disabled → the builder/preview path is unchanged.
 * (3) CASCADE FIRE — once the parent (rendered as a real Form.Item so Form.useWatch can see it) is set,
 *     the public hook is called with the mapped parentValues ({ filterParamName: value }) and enabled.
 * (4) CLEAR-ON-PARENT-CHANGE — changing the parent on the public path clears the child's own value
 *     (a stale "ward of HN" must not survive a switch to province HCM).
 *
 * Form.useWatch only sees fields registered as Form.Item, so the parent is rendered as a real Form.Item
 * (memory: feedback-antd-usewatch-tracks-registered-fields-only). Both option hooks are spied so the test
 * asserts the ROUTE + enablement, not the network.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { Form, Select } from 'antd';
import type { FormFieldDto } from '../form-types';

// Spy both option hooks. Each returns an empty settled query so the child falls back to static options
// (irrelevant here — the assertion is on WHICH hook is called and with WHAT args/enabled).
const useFieldOptionsSpy = vi.fn(() => ({ data: undefined, isLoading: false }));
const usePublicFieldOptionsSpy = vi.fn(() => ({ data: undefined, isLoading: false }));

vi.mock('../form-api', () => ({
  useFieldOptions: (...args: unknown[]) => useFieldOptionsSpy(...(args as [])),
  usePublicFieldOptions: (...args: unknown[]) => usePublicFieldOptionsSpy(...(args as [])),
}));

import { RefFieldSelect } from './ref-field-select';

function cascadeChildField(): FormFieldDto {
  return {
    id: 'child-guid',
    fieldKey: 'ward',
    type: 'EnumRef',
    labelVi: 'Phường',
    labelEn: 'Ward',
    required: false,
    displayOrder: 2,
    isActive: true,
    addedInVersion: 1,
    // Child ward depends on parent province; parent value is sent as ?p[provinceCode]=…
    dataSourceJson: JSON.stringify({
      dependencies: [{ dependsOnFieldKey: 'province', filterParamName: 'provinceCode' }],
    }),
  };
}

/**
 * Render the cascade child inside a real antd Form, with the PARENT registered as a genuine Form.Item
 * (Select) so Form.useWatch([]) actually observes the parent's value. `publicCode` toggles the path.
 */
function renderCascade(publicCode?: string) {
  // Capture the antd form instance so a test can read the child value after a parent change.
  let formRef!: ReturnType<typeof Form.useForm>[0];
  function Harness() {
    const [form] = Form.useForm();
    formRef = form;
    return (
      <Form form={form}>
        <Form.Item name="province" label="Tỉnh">
          <Select
            options={[
              { value: 'HN', label: 'Hà Nội' },
              { value: 'HCM', label: 'Hồ Chí Minh' },
            ]}
          />
        </Form.Item>
        <Form.Item name="ward" label="Phường">
          <RefFieldSelect field={cascadeChildField()} templateId="tpl-guid" publicCode={publicCode} />
        </Form.Item>
      </Form>
    );
  }
  const utils = render(<Harness />);
  return { ...utils, getForm: () => formRef };
}

/** Latest call args of a hook spy, as [id/code, field/fieldKey, parentValues, enabled]. */
function lastCall(spy: typeof useFieldOptionsSpy) {
  return spy.mock.calls[spy.mock.calls.length - 1] as unknown as [
    string,
    string,
    Record<string, string> | undefined,
    boolean,
  ];
}

describe('RefFieldSelect — public vs authed option route (C1)', () => {
  beforeEach(() => {
    useFieldOptionsSpy.mockClear();
    usePublicFieldOptionsSpy.mockClear();
  });

  it('ROUTE: public form (publicCode set) drives the public hook enabled and leaves the authed hook disabled', () => {
    renderCascade('CONTACT-FORM');

    // Public hook addressed by {code}/{fieldKey}.
    const pub = lastCall(usePublicFieldOptionsSpy);
    expect(pub[0]).toBe('CONTACT-FORM');
    expect(pub[1]).toBe('ward');

    // Authed hook is present (rules-of-hooks) but DISABLED on the public path → never fetches → no 401.
    const authed = lastCall(useFieldOptionsSpy);
    expect(authed[3]).toBe(false);
  });

  it('REGRESSION: authed builder (no publicCode) drives the authed hook enabled and disables the public hook', () => {
    renderCascade(undefined);

    // Authed hook addressed by {templateId}/{fieldId}.
    const authed = lastCall(useFieldOptionsSpy);
    expect(authed[0]).toBe('tpl-guid');
    expect(authed[1]).toBe('child-guid');

    // Public hook disabled off the public path.
    const pub = lastCall(usePublicFieldOptionsSpy);
    expect(pub[3]).toBe(false);
  });

  it('CASCADE FIRE: with the parent set, the public hook is enabled and receives the mapped parentValues', async () => {
    renderCascade('CONTACT-FORM');

    // Before any parent selection, the child gates the fetch (not all parents set) → public hook disabled.
    expect(lastCall(usePublicFieldOptionsSpy)[3]).toBe(false);

    // Select the parent province (real Form.Item → useWatch observes it).
    const provinceSelect = screen.getAllByRole('combobox')[0];
    await act(async () => {
      fireEvent.mouseDown(provinceSelect);
    });
    await act(async () => {
      fireEvent.click(screen.getByText('Hà Nội'));
    });

    // The public hook is now called with the mapped parent value and enabled.
    const pub = lastCall(usePublicFieldOptionsSpy);
    expect(pub[2]).toEqual({ provinceCode: 'HN' });
    expect(pub[3]).toBe(true);
    // The authed hook stays disabled on the public path throughout.
    expect(lastCall(useFieldOptionsSpy)[3]).toBe(false);
  });

  it('CLEAR-ON-PARENT-CHANGE: switching the parent on the public path drops the stale child value', async () => {
    const { getForm } = renderCascade('CONTACT-FORM');
    const form = getForm();

    const provinceSelect = screen.getAllByRole('combobox')[0];
    // Pick province HN, then seed a chosen ward value for HN (the "ward of HN" that must not survive).
    await act(async () => {
      fireEvent.mouseDown(provinceSelect);
    });
    await act(async () => {
      fireEvent.click(screen.getByText('Hà Nội'));
    });
    await act(async () => {
      form.setFieldValue('ward', 'ward-of-hn');
    });
    expect(form.getFieldValue('ward')).toBe('ward-of-hn');

    // Switch the parent to HCM — the clear-propagation effect must wipe the child's value.
    await act(async () => {
      fireEvent.mouseDown(provinceSelect);
    });
    await act(async () => {
      fireEvent.click(screen.getByText('Hồ Chí Minh'));
    });

    // The stale child value is cleared, AND the new parent value flows to the public hook.
    expect(form.getFieldValue('ward')).toBeUndefined();
    const pub = lastCall(usePublicFieldOptionsSpy);
    expect(pub[2]).toEqual({ provinceCode: 'HCM' });
  });
});
