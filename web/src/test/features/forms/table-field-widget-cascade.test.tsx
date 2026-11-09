// table-field-widget-cascade.test.tsx — M1 Phase-07 finalize: (a) child-column discovery via
// `parentFieldId` BE contract addition and (b) sibling-row isolation for the in-row cascade.
//
// Reviewer C-1 fix: pre-Phase-07 the FE discovered TableField child columns by fieldKey PREFIX
// (`table1.col1`), but `AddFormFieldCommandValidator` forbids dots in FieldKey → production data
// NEVER matches the prefix path → the entire Phase-07 cascade wiring was unreachable dead code.
// The fix adds `parentFieldId` to `FormFieldDto` + BE Dapper projection; this test witnesses that
// the widget now discovers children via `parentFieldId === field.id` (production path) AND falls
// back to the legacy prefix path only for fixtures without `parentFieldId` (backward-safe).
//
// Reviewer C-3 fix: no test previously witnessed sibling-row isolation. This spec mounts a 2-row
// table with cascade child, changes row-0's parent-column, and asserts row-1's child value is
// untouched — witnessing the acceptance that per-row `useRef(prevSignature)` + `updateCell(rowIdx)`
// keeps row-1's onChange out of the clear-propagation blast radius.

import { describe, it, expect, vi } from 'vitest';
import { render, act, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { FormFieldDto } from '@/features/forms/form-types';

// ──────────────────────────────────────────────────────────────────────────────
// Mocks — hoisted before the component under test resolves its imports.
// ──────────────────────────────────────────────────────────────────────────────

const useFieldOptionsSpy = vi.fn((..._args: unknown[]) => ({
  data: [
    { value: '01', label: 'Ward 01' },
    { value: '79', label: 'Ward 79' },
  ],
  isLoading: false,
}));
const usePublicFieldOptionsSpy = vi.fn((..._args: unknown[]) => ({ data: [], isLoading: false }));

vi.mock('@/features/forms/form-api', () => ({
  useFieldOptions: (...args: unknown[]) => useFieldOptionsSpy(...args),
  usePublicFieldOptions: (...args: unknown[]) => usePublicFieldOptionsSpy(...args),
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}));

// Component-under-test after mocks
import { TableFieldWidget } from '@/features/forms/builder/table-field-widget';

function withQuery(node: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client: qc }, node);
}

// Test fixture: a TableField with 2 child columns — a top-level "province" input column and a
// "ward" Ref column that cascades on province. Uses `parentFieldId` (production path) NOT the
// legacy prefix convention, so this test also witnesses the C-1 discovery-contract fix.
function makeTable(): { table: FormFieldDto; children: FormFieldDto[] } {
  const table: FormFieldDto = {
    id: 'tbl-1',
    fieldKey: 'addresses',
    type: 'TableField',
    labelVi: 'Addresses',
    labelEn: 'Addresses',
    required: false,
    displayOrder: 1,
    isActive: true,
    addedInVersion: 1,
  } as unknown as FormFieldDto;

  const provinceCol: FormFieldDto = {
    id: 'col-province',
    fieldKey: 'province',
    type: 'Text',
    labelVi: 'Province',
    labelEn: 'Province',
    required: false,
    displayOrder: 1,
    isActive: true,
    addedInVersion: 1,
    // Production wire: parentFieldId is a JSON number (BE `long?`). Widget coerces to string on
    // compare so a numeric parentFieldId matches the string-shaped `id` without a global refactor.
    parentFieldId: 'tbl-1' as unknown as number,
  } as unknown as FormFieldDto;

  const wardCol: FormFieldDto = {
    id: 'col-ward',
    fieldKey: 'ward',
    type: 'EnumRef',
    labelVi: 'Ward',
    labelEn: 'Ward',
    required: false,
    displayOrder: 2,
    isActive: true,
    addedInVersion: 1,
    parentFieldId: 'tbl-1' as unknown as number,
    dataSourceJson:
      '{"dependencies":[{"dependsOnFieldKey":"province","filterParamName":"provinceCode"}]}',
  } as unknown as FormFieldDto;

  return { table, children: [provinceCol, wardCol] };
}

describe('TableFieldWidget - id-based child discovery + sibling-row isolation (M1 finalize)', () => {
  it('discovers child columns via parentFieldId (production path, C-1 fix)', () => {
    const { table, children } = makeTable();
    const allFields = [table, ...children];

    const { container } = render(
      withQuery(
        <TableFieldWidget
          field={table}
          allFields={allFields}
          templateId="tpl-1"
          value={[{ province: 'HN', ward: '01' }]}
        />,
      ),
    );

    // If discovery worked, the ward column renders the RowRefFieldSelect for the one row, which in
    // turn invokes useFieldOptions with the province value. If discovery FAILED (regression to prefix-
    // only path), the widget would render the __key/__value fallback and no useFieldOptions call fires.
    expect(useFieldOptionsSpy).toHaveBeenCalled();
    // At least one column header rendered from the child definition (not the __key/__value fallback).
    expect(container.textContent).toContain('Ward');
  });

  it('sibling-row isolation: changing row-0 parent does NOT clear row-1 child value (C-3 fix)', () => {
    const { table, children } = makeTable();
    const allFields = [table, ...children];
    const onChange = vi.fn();

    // Two rows pre-loaded with distinct parent+child pairs. The clear-propagation for row-0 must
    // NOT touch row-1's ward value; row-1's RowRefFieldSelect has its own prevSignature ref and its
    // own onChange (updateCell(1, 'ward', ...)) - the seams that guarantee row-scope isolation.
    const initialRows = [
      { province: 'HN', ward: '01' },
      { province: 'HCM', ward: '79' },
    ];

    const { container } = render(
      withQuery(
        <TableFieldWidget
          field={table}
          allFields={allFields}
          templateId="tpl-1"
          value={initialRows}
          onChange={onChange}
        />,
      ),
    );

    // Locate row-0's province Text input - the ONLY input whose current DOM value is 'HN'. The 2nd
    // row's province Text input carries 'HCM' and must NOT be touched.
    const provinceInputs = Array.from(container.querySelectorAll('input')).filter(
      (el) => (el as HTMLInputElement).value === 'HN',
    );
    expect(provinceInputs.length).toBe(1);
    const row0Province = provinceInputs[0] as HTMLInputElement;

    // React tracks controlled-input values via a hidden internal setter to detect programmatic
    // changes. `dispatchEvent(new Event('input'))` bypasses that tracker so React swallows the
    // synthetic onChange; the only reliable way to fake a user edit in JSDOM is `fireEvent.change`
    // which uses this exact mechanism internally.
    act(() => {
      fireEvent.change(row0Province, { target: { value: 'DN' } });
    });

    // Unconditional witness: the widget MUST emit at least one onChange after a real input edit; a
    // silent no-emit would mean the C-3 assertion below never runs (the exact phantom-test failure
    // mode reviewer round-2 flagged). Assert-fail here catches that regression class first.
    expect(onChange).toHaveBeenCalled();

    // Row-scope isolation contract: whatever ELSE the widget emitted, row-1's ward MUST survive
    // verbatim across every emission. A shared prevSignature ref across rows would cause row-1's
    // cascade to clear on row-0's parent change, producing lastEmitted[1].ward === undefined.
    const lastEmitted = onChange.mock.calls[onChange.mock.calls.length - 1][0];
    expect(lastEmitted[1].ward).toBe('79');
    // Belt-and-suspenders: every SINGLE emission (not just the last) must preserve row-1's ward.
    for (const [emitted] of onChange.mock.calls) {
      expect((emitted as Array<Record<string, unknown>>)[1].ward).toBe('79');
    }
  });

  it('backward-safe: legacy fixture with prefix fieldKey (no parentFieldId) still discovered', () => {
    // Pre-existing fixture pattern - some legacy tests may use `table.child` dotted keys with no
    // parentFieldId. Widget must keep discovering them via the fallback prefix path so those tests
    // don't break on this slice.
    const table: FormFieldDto = {
      id: 'tbl-legacy',
      fieldKey: 'items',
      type: 'TableField',
      labelVi: 'Items',
      labelEn: 'Items',
      required: false,
      displayOrder: 1,
      isActive: true,
      addedInVersion: 1,
    } as unknown as FormFieldDto;

    // Legacy dotted key, no parentFieldId → must match via fallback prefix path.
    const legacyChild: FormFieldDto = {
      id: 'col-legacy',
      fieldKey: 'items.name',
      type: 'Text',
      labelVi: 'Name',
      labelEn: 'Name',
      required: false,
      displayOrder: 1,
      isActive: true,
      addedInVersion: 1,
    } as unknown as FormFieldDto;

    const { container } = render(
      withQuery(
        <TableFieldWidget
          field={table}
          allFields={[table, legacyChild]}
          value={[{ name: 'Alpha' }]}
        />,
      ),
    );

    // Legacy child rendered under its short-key column (last segment after '.').
    expect(container.textContent).toContain('Name');
  });
});
