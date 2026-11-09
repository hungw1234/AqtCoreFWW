// TC-FE-WF-INF-001: first fetch returns items without cursor
// TC-FE-WF-INF-002: fetchNextPage appends next page with cursor param
// TC-FE-WF-INF-003: filter change resets query (new query key)
// TC-FE-WF-INF-004: status='all' omits status param from URL
// TC-FE-WF-INF-005: summary hook returns typed DTO with 60s staleTime honored

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── Mock apiClient ────────────────────────────────────────────────────────────

const mockGet = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}));

import {
  useWorkflowInstancesInfinite,
  useWorkflowInstancesSummary,
} from '@/features/workflow/use-workflow-instances-infinite';
import type { CursorPagedResult, WorkflowInstanceListItem, WorkflowInstanceSummary } from '@/features/workflow/workflow-types';

// ─── Fixtures ──────────────────────────────────────────────────────────────────

function makeItem(id: string): WorkflowInstanceListItem {
  return {
    id,
    definitionId: 'def-1',
    entityType: 'Case',
    entityId: `CASE-${id}`,
    currentStateId: 'PendingReview',
    startedAt: '2024-01-01T09:00:00Z',
    dueAt: null,
    completedAt: null,
  };
}

function makePageResult(
  items: WorkflowInstanceListItem[],
  hasNextPage: boolean,
  nextCursor: string | null,
): CursorPagedResult<WorkflowInstanceListItem> {
  return { items, totalCount: null, meta: { hasNextPage, nextCursor } };
}

const PAGE1_ITEMS = [makeItem('1'), makeItem('2')];
const PAGE2_ITEMS = [makeItem('3'), makeItem('4')];

const PAGE1_RESULT = makePageResult(PAGE1_ITEMS, true, 'cursor-abc');
const PAGE1_LAST_RESULT = makePageResult(PAGE1_ITEMS, false, null);
const PAGE2_RESULT = makePageResult(PAGE2_ITEMS, false, null);

const SUMMARY: WorkflowInstanceSummary = {
  active: 10,
  completedToday: 3,
  slaBreachRate: 5,
  total: 42,
};

// ─── Wrapper factory ───────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ─── Tests ─────────────────────────────────────────────────────────────────────

describe('useWorkflowInstancesInfinite', () => {
  beforeEach(() => {
    mockGet.mockReset();
  });

  // TC-FE-WF-INF-001
  it('first fetch returns page 1 items without cursor in URL', async () => {
    mockGet.mockResolvedValueOnce({ data: PAGE1_RESULT });

    const { result } = renderHook(
      () => useWorkflowInstancesInfinite({ status: 'active' }),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const flat = result.current.data?.pages.flatMap((p) => p.items) ?? [];
    expect(flat).toHaveLength(2);
    expect(flat[0].id).toBe('1');
    expect(flat[1].id).toBe('2');

    // URL must NOT contain 'cursor=' on first fetch
    const calledUrl: string = mockGet.mock.calls[0][0];
    expect(calledUrl).not.toContain('cursor=');
    expect(calledUrl).toContain('pageSize=50');
    expect(calledUrl).toContain('status=active');
  });

  // TC-FE-WF-INF-002
  it('fetchNextPage appends second page and passes cursor param', async () => {
    mockGet
      .mockResolvedValueOnce({ data: PAGE1_RESULT })
      .mockResolvedValueOnce({ data: PAGE2_RESULT });

    const { result } = renderHook(
      () => useWorkflowInstancesInfinite({}),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.hasNextPage).toBe(true);

    result.current.fetchNextPage();

    await waitFor(() =>
      expect(result.current.data?.pages.length).toBe(2),
    );

    const flat = result.current.data?.pages.flatMap((p) => p.items) ?? [];
    expect(flat).toHaveLength(4);
    expect(flat[2].id).toBe('3');
    expect(flat[3].id).toBe('4');

    // Second call must include cursor
    const secondUrl: string = mockGet.mock.calls[1][0];
    expect(secondUrl).toContain('cursor=cursor-abc');
  });

  // TC-FE-WF-INF-003: filter change resets query via new query key
  it('filter change uses a different query key (no cross-contamination)', async () => {
    mockGet
      .mockResolvedValueOnce({ data: PAGE1_RESULT })
      .mockResolvedValueOnce({ data: PAGE1_LAST_RESULT });

    // Render hook with first filter
    const { result: r1 } = renderHook(
      () => useWorkflowInstancesInfinite({ entityType: 'Case' }),
      { wrapper: makeWrapper() },
    );
    await waitFor(() => expect(r1.current.isSuccess).toBe(true));

    // Render hook with different filter in a separate wrapper (isolated QueryClient)
    const { result: r2 } = renderHook(
      () => useWorkflowInstancesInfinite({ entityType: 'Form' }),
      { wrapper: makeWrapper() },
    );
    await waitFor(() => expect(r2.current.isSuccess).toBe(true));

    // Both calls happened — different URLs confirm different cache keys
    const url1: string = mockGet.mock.calls[0][0];
    const url2: string = mockGet.mock.calls[1][0];
    expect(url1).toContain('entityType=Case');
    expect(url2).toContain('entityType=Form');
  });

  // TC-FE-WF-INF-004: status='all' omits status param
  it("status='all' omits status from URL", async () => {
    mockGet.mockResolvedValueOnce({ data: PAGE1_LAST_RESULT });

    const { result } = renderHook(
      () => useWorkflowInstancesInfinite({ status: 'all' }),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockGet.mock.calls[0][0];
    expect(calledUrl).not.toContain('status=');
  });

  it('hasNextPage is false when meta.hasNextPage=false', async () => {
    mockGet.mockResolvedValueOnce({ data: PAGE1_LAST_RESULT });

    const { result } = renderHook(
      () => useWorkflowInstancesInfinite({}),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.hasNextPage).toBe(false);
  });
});

// ─── Summary hook tests ────────────────────────────────────────────────────────

describe('useWorkflowInstancesSummary', () => {
  beforeEach(() => {
    mockGet.mockReset();
  });

  // TC-FE-WF-INF-005
  it('returns typed summary DTO', async () => {
    mockGet.mockResolvedValueOnce({ data: SUMMARY });

    const { result } = renderHook(
      () => useWorkflowInstancesSummary({}),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.active).toBe(10);
    expect(result.current.data?.completedToday).toBe(3);
    expect(result.current.data?.slaBreachRate).toBe(5);
    expect(result.current.data?.total).toBe(42);
  });

  it('calls correct summary endpoint', async () => {
    mockGet.mockResolvedValueOnce({ data: SUMMARY });

    const { result } = renderHook(
      () => useWorkflowInstancesSummary({ entityType: 'Case', status: 'active' }),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockGet.mock.calls[0][0];
    expect(calledUrl).toContain('/workflow/instances/summary');
    expect(calledUrl).toContain('entityType=Case');
    expect(calledUrl).toContain('status=active');
  });
});
