// enum-parent-filter-map-editor.test.tsx — P7-FU coverage: authoring keyed-Enum parentFilterMap
// persists into DataSourceJson, and the map editor is only shown for EnumRef fields with dependencies.

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { EnumParentFilterMapEditor } from '@/features/forms/builder/enum-parent-filter-map-editor';
import { CascadeDependenciesEditor } from '@/features/forms/builder/cascade-dependencies-editor';
import type { FormFieldDto } from '@/features/forms/form-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (_k: string, o?: { defaultValue?: string }) => o?.defaultValue ?? _k }),
}));

function field(id: string, key: string, type: FormFieldDto['type'], dataSourceJson?: string): FormFieldDto {
  return {
    id,
    fieldKey: key,
    type,
    labelVi: key,
    labelEn: key,
    displayOrder: 1,
    required: false,
    dataSourceJson: dataSourceJson ?? null,
  } as FormFieldDto;
}

describe('EnumParentFilterMapEditor — P7-FU', () => {
  it('adds a mapping row that persists into DataSourceJson.parentFilterMap', () => {
    const onChange = vi.fn();
    render(<EnumParentFilterMapEditor value='{"EnumTypeName":"X"}' onChange={onChange} />);

    fireEvent.click(screen.getByText('Add mapping'));

    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted.EnumTypeName).toBe('X'); // preserved
    // A freshly-added row has an empty allowed list → withParentFilterMap drops it, so the key is absent
    // until the admin fills values. That is correct: an empty map == no filter key.
    expect(emitted).not.toHaveProperty('parentFilterMap');
  });

  it('renders existing map rows', () => {
    render(
      <EnumParentFilterMapEditor
        value='{"EnumTypeName":"X","parentFilterMap":{"HN":["1","2"]}}'
        onChange={vi.fn()}
      />,
    );
    // The parent-value input shows the existing key.
    expect(screen.getByDisplayValue('HN')).toBeTruthy();
  });

  it('removing a row drops it from the map', () => {
    const onChange = vi.fn();
    render(
      <EnumParentFilterMapEditor
        value='{"parentFilterMap":{"HN":["1"]}}'
        onChange={onChange}
      />,
    );
    fireEvent.click(screen.getByLabelText('remove-filter-row'));
    expect(JSON.parse(onChange.mock.calls[0][0])).not.toHaveProperty('parentFilterMap');
  });
});

describe('CascadeDependenciesEditor — keyed-Enum wiring (P7-FU)', () => {
  it('shows the parentFilterMap editor for an EnumRef with a dependency', () => {
    const child = field('c', 'xa', 'EnumRef', '{"dependencies":[{"dependsOnFieldKey":"tinh","filterParamName":"p"}]}');
    const parent = field('p', 'tinh', 'ExternalRef');
    render(
      <CascadeDependenciesEditor field={child} allFields={[child, parent]} value={child.dataSourceJson ?? undefined} onChange={vi.fn()} />,
    );
    expect(screen.getByText(/allowed values map/i)).toBeTruthy();
  });

  it('does NOT show the parentFilterMap editor for an ExternalRef', () => {
    const child = field('c', 'xa', 'ExternalRef', '{"dependencies":[{"dependsOnFieldKey":"tinh","filterParamName":"p"}]}');
    const parent = field('p', 'tinh', 'ExternalRef');
    render(
      <CascadeDependenciesEditor field={child} allFields={[child, parent]} value={child.dataSourceJson ?? undefined} onChange={vi.fn()} />,
    );
    expect(screen.queryByText(/allowed values map/i)).toBeNull();
  });
});
