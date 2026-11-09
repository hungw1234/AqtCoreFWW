// cascade-dependencies.test.ts — P5 unit coverage for the pure cascade helpers (spike §1/§2).

import { describe, it, expect } from 'vitest';
import {
  parseCascadeDependencies,
  hasCascadeDependencies,
  buildParentValues,
} from '@/features/forms/cascade-dependencies';

describe('parseCascadeDependencies', () => {
  it('returns [] for absent/empty/malformed json (backward path)', () => {
    expect(parseCascadeDependencies(null)).toEqual([]);
    expect(parseCascadeDependencies(undefined)).toEqual([]);
    expect(parseCascadeDependencies('')).toEqual([]);
    expect(parseCascadeDependencies('not json')).toEqual([]);
    expect(parseCascadeDependencies('{"ApiUrl":"x"}')).toEqual([]);
    expect(parseCascadeDependencies('{"dependencies":[]}')).toEqual([]);
  });

  it('parses valid dependency entries', () => {
    const json = JSON.stringify({
      ApiUrl: 'https://x',
      dependencies: [
        { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
        { dependsOnFieldKey: 'huyen', filterParamName: 'districtCode' },
      ],
    });
    expect(parseCascadeDependencies(json)).toEqual([
      { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
      { dependsOnFieldKey: 'huyen', filterParamName: 'districtCode' },
    ]);
  });

  it('drops malformed entries (missing keys)', () => {
    const json = JSON.stringify({
      dependencies: [
        { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
        { dependsOnFieldKey: '', filterParamName: 'x' },
        { filterParamName: 'y' },
        { dependsOnFieldKey: 'ok' },
      ],
    });
    expect(parseCascadeDependencies(json)).toEqual([
      { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
    ]);
  });

  it('hasCascadeDependencies reflects presence', () => {
    expect(hasCascadeDependencies({ dataSourceJson: null })).toBe(false);
    expect(
      hasCascadeDependencies({
        dataSourceJson: '{"dependencies":[{"dependsOnFieldKey":"a","filterParamName":"b"}]}',
      }),
    ).toBe(true);
  });
});

describe('buildParentValues', () => {
  const deps = [
    { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
    { dependsOnFieldKey: 'huyen', filterParamName: 'districtCode' },
  ];

  it('all parents set → full map + allParentsSet true', () => {
    const r = buildParentValues(deps, { tinh: 'HN', huyen: 'Q1' });
    expect(r.parentValues).toEqual({ provinceCode: 'HN', districtCode: 'Q1' });
    expect(r.allParentsSet).toBe(true);
  });

  it('one parent missing → allParentsSet false (multi-parent gate)', () => {
    const r = buildParentValues(deps, { tinh: 'HN' });
    expect(r.allParentsSet).toBe(false);
    expect(r.parentValues).toEqual({ provinceCode: 'HN' });
  });

  it('empty-string parent counts as unset', () => {
    const r = buildParentValues(deps, { tinh: 'HN', huyen: '' });
    expect(r.allParentsSet).toBe(false);
  });

  it('no deps → allParentsSet false (nothing to gate on, stays on static path)', () => {
    const r = buildParentValues([], { tinh: 'HN' });
    expect(r.allParentsSet).toBe(false);
    expect(r.parentValues).toEqual({});
  });

  it('coerces non-string parent values to string', () => {
    const r = buildParentValues([{ dependsOnFieldKey: 'n', filterParamName: 'num' }], { n: 42 });
    expect(r.parentValues).toEqual({ num: '42' });
    expect(r.allParentsSet).toBe(true);
  });
});
