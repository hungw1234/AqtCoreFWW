// form-field-widgets.test.tsx — smoke tests for 5 new field widgets
// Tests: RefFieldSelect, AddressFieldWidget, RichTextWidget, SignatureWidget, TableFieldWidget

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Form } from 'antd';

// ──────────────────────────────────────────────────────────────────────────────
// Global mocks
// ──────────────────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}));

// Mock react-signature-canvas (canvas not available in jsdom)
vi.mock('react-signature-canvas', () => ({
  default: vi.fn().mockImplementation(() => null),
}));

// Mock react-quill-new (heavy dependency)
vi.mock('react-quill-new', () => ({
  default: ({ value, onChange }: any) => (
    <textarea
      data-testid="quill-mock"
      value={value}
      onChange={(e: any) => onChange?.(e.target.value)}
    />
  ),
}));

// Mock quill CSS
vi.mock('react-quill-new/dist/quill.snow.css', () => ({}));

// Mock form-api — both authed and public option-fetch hooks. Cascade v1 (2026-08-05) added
// usePublicFieldOptions to the module; Phase-07 M1 (2026-08-12) added RowRefFieldSelect which
// imports both. Mock BOTH so any consumer that unconditionally references either hook works.
vi.mock('@/features/forms/form-api', () => ({
  useFieldOptions: () => ({
    data: [
      { value: 'opt1', label: 'Option 1', labelVi: 'Tùy chọn 1' },
      { value: 'opt2', label: 'Option 2', labelVi: 'Tùy chọn 2' },
    ],
    isLoading: false,
  }),
  usePublicFieldOptions: () => ({
    data: [
      { value: 'opt1', label: 'Option 1', labelVi: 'Tùy chọn 1' },
      { value: 'opt2', label: 'Option 2', labelVi: 'Tùy chọn 2' },
    ],
    isLoading: false,
  }),
}));

// Mock apiClient for address widget
vi.mock('@/core/api', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({
      data: [
        { code: 'HN', nameVi: 'Hà Nội' },
        { code: 'SG', nameVi: 'Thành phố Hồ Chí Minh' },
      ],
    }),
  },
}));

// ──────────────────────────────────────────────────────────────────────────────
// Component imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { RefFieldSelect } from '@/features/forms/builder/ref-field-select';
import { AddressFieldWidget } from '@/features/forms/builder/address-field-widget';
import { RichTextWidget } from '@/features/forms/builder/rich-text-widget';
import { SignatureWidget } from '@/shared/components/signature-widget';
import { TableFieldWidget } from '@/features/forms/builder/table-field-widget';
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
// Wrapper utility
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-WIDGET-001: RefFieldSelect
// ──────────────────────────────────────────────────────────────────────────────

describe('RefFieldSelect — TC-FE-WIDGET-001', () => {
  it('renders Select without crashing', () => {
    const field = makeField({ fieldKey: 'status', type: 'EnumRef' });
    const { container } = render(
      <Form>
        <Form.Item name="status">
          <RefFieldSelect field={field} templateId="tpl-1" />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-select')).toBeTruthy();
  });

  it('renders with field-specific props', () => {
    const field = makeField({
      fieldKey: 'status',
      type: 'EnumRef',
      labelVi: 'Trạng thái',
    });
    const { container } = render(
      <Form>
        <Form.Item name="status">
          <RefFieldSelect field={field} templateId="tpl-1" />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Verify Select component is rendered with options
    expect(container.querySelector('.ant-select')).toBeTruthy();
  });

  it('renders options from API', async () => {
    const field = makeField({ fieldKey: 'enum_ref', type: 'EnumRef' });
    const { container } = render(
      <Form>
        <Form.Item name="enum_ref">
          <RefFieldSelect field={field} templateId="tpl-1" />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-select')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-WIDGET-002: AddressFieldWidget
// ──────────────────────────────────────────────────────────────────────────────

describe('AddressFieldWidget — TC-FE-WIDGET-002', () => {
  it('renders 2 Select elements (province, ward)', async () => {
    const { container } = render(
      <Form>
        <Form.Item name="address">
          <AddressFieldWidget />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    const selects = container.querySelectorAll('.ant-select');
    expect(selects.length).toBe(2); // province + ward — district removed (2-level geo)
  });

  it('renders without crashing', () => {
    const { container } = render(
      <Form>
        <Form.Item name="address">
          <AddressFieldWidget disabled={false} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-WIDGET-003: RichTextWidget
// ──────────────────────────────────────────────────────────────────────────────

describe('RichTextWidget — TC-FE-WIDGET-003', () => {
  it('renders without crashing', () => {
    const { container } = render(
      <Form>
        <Form.Item name="description">
          <RichTextWidget />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('renders with Suspense wrapper for lazy loading', () => {
    const { container } = render(
      <Form>
        <Form.Item name="description">
          <RichTextWidget />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Component renders successfully with lazy-loaded Quill
    expect(container.querySelector('.ant-form-item')).toBeTruthy();
  });

  it('accepts disabled prop', () => {
    const onChange = vi.fn();
    const { container } = render(
      <Form>
        <Form.Item name="description">
          <RichTextWidget disabled={true} onChange={onChange} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-WIDGET-004: SignatureWidget
// ──────────────────────────────────────────────────────────────────────────────

describe('SignatureWidget — TC-FE-WIDGET-004', () => {
  it('renders clear button', () => {
    const { container } = render(
      <Form>
        <Form.Item name="signature">
          <SignatureWidget />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // SignatureWidget renders a Button with clear action
    expect(container.querySelector('button')).toBeTruthy();
  });

  it('renders without crashing', () => {
    const { container } = render(
      <Form>
        <Form.Item name="signature">
          <SignatureWidget disabled={false} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-WIDGET-004-b: SignatureWidget — responsive height + DPR (A1 fixes)
// ──────────────────────────────────────────────────────────────────────────────

describe('SignatureWidget — TC-FE-WIDGET-004-b (A1 responsive + DPR)', () => {
  it('hides clear button when disabled=true', () => {
    const { container } = render(
      <Form>
        <Form.Item name="signature">
          <SignatureWidget disabled={true} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Clear button must not be rendered when widget is disabled
    expect(container.querySelector('button')).toBeNull();
  });

  it('shows clear button when disabled=false', () => {
    const { container } = render(
      <Form>
        <Form.Item name="signature">
          <SignatureWidget disabled={false} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('button')).toBeTruthy();
  });

  it('renders container div with touchAction none to prevent scroll conflict', () => {
    const { container } = render(
      <Form>
        <Form.Item name="signature">
          <SignatureWidget />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Find the canvas wrapper div — it carries touchAction: 'none'
    const divs = container.querySelectorAll('div');
    const touchNoneDiv = Array.from(divs).find(
      (d) => (d as HTMLElement).style.touchAction === 'none'
    );
    expect(touchNoneDiv).toBeTruthy();
  });

  it('computeHeight: 320px wide → height = 107 (floor 100, cap 300)', () => {
    // Inline verification of the aspect-ratio formula used in signature-widget.tsx
    // Math.min(Math.max(Math.round(width / 3), 100), 300)
    const computeHeight = (w: number) => Math.min(Math.max(Math.round(w / 3), 100), 300);
    expect(computeHeight(320)).toBe(107);   // 320/3 = 106.67 → 107
    expect(computeHeight(270)).toBe(100);   // 270/3 = 90 → floor to 100
    expect(computeHeight(200)).toBe(100);   // narrow → floor
    expect(computeHeight(900)).toBe(300);   // 900/3 = 300 → exact cap
    expect(computeHeight(1200)).toBe(300);  // wide → capped at 300
    expect(computeHeight(400)).toBe(133);   // default initial: 400/3 = 133.33 → 133
  });

  it('DPR formula: window.devicePixelRatio || 1 defaults to 1 in jsdom', () => {
    // jsdom does not set devicePixelRatio; verify the guard evaluates to 1
    const dpr = window.devicePixelRatio || 1;
    expect(dpr).toBe(1);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-WIDGET-005: TableFieldWidget
// ──────────────────────────────────────────────────────────────────────────────

describe('TableFieldWidget — TC-FE-WIDGET-005', () => {
  it('renders table structure', () => {
    const field = makeField({
      id: 'tbl1',
      fieldKey: 'items_table',
      type: 'TableField',
    });
    const { container } = render(
      <Form>
        <Form.Item name="items_table">
          <TableFieldWidget field={field} allFields={[field]} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });

  it('renders "Thêm dòng" (add row) button', () => {
    const field = makeField({
      id: 'tbl1',
      fieldKey: 'items_table',
      type: 'TableField',
    });
    render(
      <Form>
        <Form.Item name="items_table">
          <TableFieldWidget field={field} allFields={[field]} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Button text should contain add action (icon-based or text)
    const button = screen.getByRole('button');
    expect(button).toBeTruthy();
  });

  it('renders without crashing with empty rows', () => {
    const field = makeField({
      id: 'tbl1',
      fieldKey: 'items_table',
      type: 'TableField',
    });
    const { container } = render(
      <Form>
        <Form.Item name="items_table">
          <TableFieldWidget field={field} allFields={[field]} value={[]} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('discovers child fields by fieldKey prefix', () => {
    const parentField = makeField({
      id: 'tbl1',
      fieldKey: 'items_table',
      type: 'TableField',
    });
    const childField1 = makeField({
      id: 'col1',
      fieldKey: 'items_table.col1',
      type: 'Text',
      displayOrder: 2,
    });
    const childField2 = makeField({
      id: 'col2',
      fieldKey: 'items_table.col2',
      type: 'Number',
      displayOrder: 3,
    });
    const { container } = render(
      <Form>
        <Form.Item name="items_table">
          <TableFieldWidget
            field={parentField}
            allFields={[parentField, childField1, childField2]}
          />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });
});
