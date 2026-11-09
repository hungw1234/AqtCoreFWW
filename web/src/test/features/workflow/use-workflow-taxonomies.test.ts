// TC-FE-WFT-001: useWorkflowStateTaxonomies fetches from /admin/master-data/workflow-taxonomy/states
// TC-FE-WFT-002: useWorkflowActionTaxonomies fetches from /admin/master-data/workflow-taxonomy/actions
// TC-FE-WFT-003: toTaxonomyOptions projects { code, nameVi } to { label: "Code — NameVi", value: code }
// TC-FE-WFT-004: isCustomCode returns true only when code is absent from dictionary (case-insensitive)
// TC-FE-WFT-005: hooks tolerate empty response (return [] not undefined)

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── Mock apiClient ──────────────────────────────────────────────────────────

const mockGet = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}));

import {
  useWorkflowStateTaxonomies,
  useWorkflowActionTaxonomies,
  toTaxonomyOptions,
  isCustomCode,
  type WorkflowStateTaxonomyDto,
  type WorkflowActionTaxonomyDto,
} from '@/features/workflow/use-workflow-taxonomies';

// ─── Fixtures ────────────────────────────────────────────────────────────────

const STATE_ROWS: WorkflowStateTaxonomyDto[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    code: 'Draft',
    nameVi: 'Nháp',
    nameEn: 'Draft',
    i18nKey: null,
    icon: 'EditOutlined',
    color: '#808080',
    category: 'Initial',
    sortOrder: 1,
    isActive: true,
    deprecatedReason: null,
    rowVersion: 'AAAAAAAAB/E=',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    code: 'Approved',
    nameVi: 'Đã duyệt',
    nameEn: 'Approved',
    i18nKey: null,
    icon: null,
    color: null,
    category: 'Terminal',
    sortOrder: 5,
    isActive: true,
    deprecatedReason: null,
    rowVersion: 'AAAAAAAAB/I=',
  },
];

const ACTION_ROWS: WorkflowActionTaxonomyDto[] = [
  {
    id: '33333333-3333-3333-3333-333333333333',
    code: 'Submit',
    nameVi: 'Nộp',
    nameEn: 'Submit',
    i18nKey: null,
    icon: null,
    sortOrder: 1,
    isActive: true,
    deprecatedReason: null,
    rowVersion: 'AAAAAAAAB/M=',
  },
];

// ─── Wrapper factory ─────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('useWorkflowStateTaxonomies', () => {
  beforeEach(() => mockGet.mockReset());

  // TC-FE-WFT-001
  it('fetches /admin/master-data/workflow-taxonomy/states and returns array', async () => {
    mockGet.mockResolvedValueOnce({ data: STATE_ROWS });

    const { result } = renderHook(() => useWorkflowStateTaxonomies(), { wrapper: makeWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGet).toHaveBeenCalledWith('/admin/master-data/workflow-taxonomy/states');
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0].code).toBe('Draft');
  });

  // TC-FE-WFT-005 (states branch)
  it('returns empty array when backend payload is null/undefined', async () => {
    mockGet.mockResolvedValueOnce({ data: undefined });

    const { result } = renderHook(() => useWorkflowStateTaxonomies(), { wrapper: makeWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });
});

describe('useWorkflowActionTaxonomies', () => {
  beforeEach(() => mockGet.mockReset());

  // TC-FE-WFT-002
  it('fetches /admin/master-data/workflow-taxonomy/actions and returns array', async () => {
    mockGet.mockResolvedValueOnce({ data: ACTION_ROWS });

    const { result } = renderHook(() => useWorkflowActionTaxonomies(), { wrapper: makeWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGet).toHaveBeenCalledWith('/admin/master-data/workflow-taxonomy/actions');
    expect(result.current.data?.[0].code).toBe('Submit');
  });
});

describe('toTaxonomyOptions', () => {
  // TC-FE-WFT-003
  it('projects code+nameVi into antd Select options', () => {
    const out = toTaxonomyOptions(STATE_ROWS);
    expect(out).toEqual([
      { label: 'Draft — Nháp', value: 'Draft' },
      { label: 'Approved — Đã duyệt', value: 'Approved' },
    ]);
  });

  it('handles empty array', () => {
    expect(toTaxonomyOptions([])).toEqual([]);
  });
});

describe('isCustomCode', () => {
  // TC-FE-WFT-004
  it('returns false when code matches a dictionary row (case-insensitive)', () => {
    expect(isCustomCode('Draft', STATE_ROWS)).toBe(false);
    expect(isCustomCode('DRAFT', STATE_ROWS)).toBe(false);
    expect(isCustomCode('draft', STATE_ROWS)).toBe(false);
  });

  it('returns true when code is absent from dictionary', () => {
    expect(isCustomCode('MyCustomState', STATE_ROWS)).toBe(true);
  });

  it('returns false for empty/null input (nothing to warn about)', () => {
    expect(isCustomCode('', STATE_ROWS)).toBe(false);
    expect(isCustomCode(null, STATE_ROWS)).toBe(false);
    expect(isCustomCode(undefined, STATE_ROWS)).toBe(false);
  });
});
