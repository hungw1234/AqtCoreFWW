// cascade-builder.test.ts — P7 unit coverage for the builder cascade helpers.

import { describe, it, expect } from 'vitest';
import {
  getDependencies,
  withDependencies,
  upsertDependency,
  removeDependency,
  wouldCreateCycle,
  buildAdjacency,
  getParentFilterMap,
  withParentFilterMap,
} from '@/features/forms/cascade-builder';

describe('withDependencies / getDependencies round-trip', () => {
  it('adds dependencies while preserving other config keys', () => {
    const json = withDependencies('{"ApiUrl":"https://x","CacheSeconds":60}', [
      { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
    ]);
    const parsed = JSON.parse(json);
    expect(parsed.ApiUrl).toBe('https://x');
    expect(parsed.CacheSeconds).toBe(60);
    expect(getDependencies(json)).toEqual([
      { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' },
    ]);
  });

  it('empty list drops the dependencies key (returns to static path)', () => {
    const json = withDependencies(
      '{"ApiUrl":"https://x","dependencies":[{"dependsOnFieldKey":"a","filterParamName":"b"}]}',
      [],
    );
    expect(JSON.parse(json)).not.toHaveProperty('dependencies');
    expect(getDependencies(json)).toEqual([]);
  });
});

describe('upsertDependency / removeDependency', () => {
  it('upsert adds a new edge', () => {
    const r = upsertDependency([], { dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' });
    expect(r).toEqual([{ dependsOnFieldKey: 'tinh', filterParamName: 'provinceCode' }]);
  });

  it('upsert replaces the edge for the same parent (no duplicate)', () => {
    const r = upsertDependency(
      [{ dependsOnFieldKey: 'tinh', filterParamName: 'old' }],
      { dependsOnFieldKey: 'tinh', filterParamName: 'new' },
    );
    expect(r).toEqual([{ dependsOnFieldKey: 'tinh', filterParamName: 'new' }]);
  });

  it('upsert ignores a blank parent key', () => {
    const r = upsertDependency([], { dependsOnFieldKey: '', filterParamName: 'x' });
    expect(r).toEqual([]);
  });

  it('remove drops the edge for a parent', () => {
    const r = removeDependency(
      [
        { dependsOnFieldKey: 'tinh', filterParamName: 'p' },
        { dependsOnFieldKey: 'huyen', filterParamName: 'd' },
      ],
      'tinh',
    );
    expect(r).toEqual([{ dependsOnFieldKey: 'huyen', filterParamName: 'd' }]);
  });
});

describe('wouldCreateCycle (soft client check)', () => {
  it('flags a self-dependency', () => {
    expect(wouldCreateCycle({}, 'a', 'a')).toBe(true);
  });

  it('flags a direct 2-cycle: B→A exists, adding A→B', () => {
    // adjacency: B depends on A (B→A). Now the admin configures A to depend on B (A→B).
    const adj = { b: ['a'] };
    expect(wouldCreateCycle(adj, 'a', 'b')).toBe(true);
  });

  it('flags a transitive cycle: C→B, B→A, adding A→C', () => {
    const adj = { c: ['b'], b: ['a'] };
    expect(wouldCreateCycle(adj, 'a', 'c')).toBe(true);
  });

  it('allows an acyclic add: A exists, adding B→A', () => {
    const adj = {};
    expect(wouldCreateCycle(adj, 'b', 'a')).toBe(false);
  });

  it('buildAdjacency reads edges from field dataSourceJson', () => {
    const adj = buildAdjacency([
      { fieldKey: 'tinh', dataSourceJson: null },
      {
        fieldKey: 'xa',
        dataSourceJson: '{"dependencies":[{"dependsOnFieldKey":"tinh","filterParamName":"p"}]}',
      },
    ]);
    expect(adj).toEqual({ xa: ['tinh'] });
  });
});

describe('parentFilterMap round-trip (keyed-Enum)', () => {
  it('getParentFilterMap parses a valid map', () => {
    expect(
      getParentFilterMap('{"EnumTypeName":"X","parentFilterMap":{"HN":["1","2"],"HCM":["3"]}}'),
    ).toEqual({ HN: ['1', '2'], HCM: ['3'] });
  });

  it('getParentFilterMap returns {} for absent/malformed', () => {
    expect(getParentFilterMap(null)).toEqual({});
    expect(getParentFilterMap('{"EnumTypeName":"X"}')).toEqual({});
    expect(getParentFilterMap('not json')).toEqual({});
  });

  it('withParentFilterMap adds the map preserving other keys', () => {
    const json = withParentFilterMap('{"EnumTypeName":"X"}', { HN: ['1', '2'] });
    const parsed = JSON.parse(json);
    expect(parsed.EnumTypeName).toBe('X');
    expect(parsed.parentFilterMap).toEqual({ HN: ['1', '2'] });
  });

  it('withParentFilterMap drops empty rows and the key when nothing remains', () => {
    const json = withParentFilterMap('{"EnumTypeName":"X","parentFilterMap":{"HN":["1"]}}', {
      HN: [],
      '': ['x'],
    });
    expect(JSON.parse(json)).not.toHaveProperty('parentFilterMap');
  });
});
