// cascade-dependencies-editor.test.tsx — P7 component coverage: add/remove a dependency persists into
// DataSourceJson, and configuring an edge that closes a cycle shows the soft client warning.

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { CascadeDependenciesEditor } from '@/features/forms/builder/cascade-dependencies-editor';
import type { FormFieldDto } from '@/features/forms/form-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (_k: string, o?: { defaultValue?: string }) => o?.defaultValue ?? _k }),
}));

function field(id: string, key: string, dataSourceJson?: string): FormFieldDto {
  return {
    id,
    fieldKey: key,
    type: 'ExternalRef',
    labelVi: key,
    labelEn: key,
    displayOrder: 1,
    required: false,
    dataSourceJson: dataSourceJson ?? null,
  } as FormFieldDto;
}

describe('CascadeDependenciesEditor — P7', () => {
  it('add dependency persists into DataSourceJson.dependencies', () => {
    const onChange = vi.fn();
    const child = field('c', 'xa', '{"ApiUrl":"https://x"}');
    const parent = field('p', 'tinh');

    render(
      <CascadeDependenciesEditor field={child} allFields={[child, parent]} value={child.dataSourceJson ?? undefined} onChange={onChange} />,
    );

    fireEvent.click(screen.getByText('Add dependency'));

    expect(onChange).toHaveBeenCalledTimes(1);
    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted.ApiUrl).toBe('https://x'); // other keys preserved
    expect(emitted.dependencies).toEqual([{ dependsOnFieldKey: 'tinh', filterParamName: 'tinh' }]);
  });

  it('remove dependency drops the key (back to static path)', () => {
    const onChange = vi.fn();
    const child = field('c', 'xa', '{"ApiUrl":"https://x","dependencies":[{"dependsOnFieldKey":"tinh","filterParamName":"p"}]}');
    const parent = field('p', 'tinh');

    render(
      <CascadeDependenciesEditor field={child} allFields={[child, parent]} value={child.dataSourceJson ?? undefined} onChange={onChange} />,
    );

    fireEvent.click(screen.getByLabelText('remove-dependency'));

    const emitted = JSON.parse(onChange.mock.calls[0][0]);
    expect(emitted).not.toHaveProperty('dependencies');
  });

  it('shows a soft cycle warning when the configured edge closes a cycle', () => {
    // Parent field "tinh" already depends on child "xa" (tinh→xa). Now "xa" declares a dep on "tinh"
    // (xa→tinh) → a 2-cycle. The editor should warn (soft; BE still hard-rejects at publish).
    const child = field('c', 'xa', '{"dependencies":[{"dependsOnFieldKey":"tinh","filterParamName":"p"}]}');
    const parent = field('p', 'tinh', '{"dependencies":[{"dependsOnFieldKey":"xa","filterParamName":"q"}]}');

    render(
      <CascadeDependenciesEditor field={child} allFields={[child, parent]} value={child.dataSourceJson ?? undefined} onChange={vi.fn()} />,
    );

    expect(screen.getByText(/cycle/i)).toBeTruthy();
  });

  it('no warning for an acyclic configuration', () => {
    const child = field('c', 'xa', '{"dependencies":[{"dependsOnFieldKey":"tinh","filterParamName":"p"}]}');
    const parent = field('p', 'tinh');

    render(
      <CascadeDependenciesEditor field={child} allFields={[child, parent]} value={child.dataSourceJson ?? undefined} onChange={vi.fn()} />,
    );

    expect(screen.queryByText(/cycle/i)).toBeNull();
  });
});
