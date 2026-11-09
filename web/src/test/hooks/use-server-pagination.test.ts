import { describe, it, expect, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useServerPagination } from '@/core/hooks/use-server-pagination';

// Mock TanStack Router — useSearch returns controlled search params,
// useNavigate returns a spy so we can assert URL updates.
const mockSearch: Record<string, unknown> = {};
const mockNavigate = vi.fn();

vi.mock('@tanstack/react-router', () => ({
  useSearch: () => mockSearch,
  useNavigate: () => mockNavigate,
}));

function resetSearch(overrides: Record<string, unknown> = {}) {
  // Clear then repopulate shared mock object
  Object.keys(mockSearch).forEach((k) => delete mockSearch[k]);
  Object.assign(mockSearch, overrides);
  mockNavigate.mockClear();
}

describe('useServerPagination — initial state', () => {
  it('defaults to page 1 and pageSize 20 when search params are empty', () => {
    resetSearch();
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.page).toBe(1);
    expect(result.current.pageSize).toBe(20);
  });

  it('respects custom defaultPageSize argument', () => {
    resetSearch();
    const { result } = renderHook(() => useServerPagination(50));
    expect(result.current.pageSize).toBe(50);
  });

  it('reads page and pageSize from existing search params', () => {
    resetSearch({ page: '3', pageSize: '10' });
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.page).toBe(3);
    expect(result.current.pageSize).toBe(10);
  });
});

describe('useServerPagination — toQueryParams', () => {
  it('returns { pageNumber, pageSize } matching current state', () => {
    resetSearch({ page: '2', pageSize: '25' });
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.toQueryParams()).toEqual({ pageNumber: 2, pageSize: 25 });
  });

  it('returns pageNumber: 1 for default state', () => {
    resetSearch();
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.toQueryParams()).toEqual({ pageNumber: 1, pageSize: 20 });
  });
});

describe('useServerPagination — setPage', () => {
  it('calls navigate with updated page keeping current pageSize', () => {
    resetSearch({ page: '1', pageSize: '20' });
    const { result } = renderHook(() => useServerPagination());

    act(() => {
      result.current.setPage(4);
    });

    expect(mockNavigate).toHaveBeenCalledOnce();
    const callArg = mockNavigate.mock.calls[0][0];
    // The navigate search updater merges page=4 into existing params
    const updatedSearch = callArg.search({ page: 1, pageSize: 20 });
    expect(updatedSearch.page).toBe(4);
    expect(callArg.replace).toBe(true);
  });
});

describe('useServerPagination — setPageSize', () => {
  it('resets page to 1 when page size changes', () => {
    resetSearch({ page: '3', pageSize: '20' });
    const { result } = renderHook(() => useServerPagination());

    act(() => {
      result.current.setPageSize(50);
    });

    expect(mockNavigate).toHaveBeenCalledOnce();
    const callArg = mockNavigate.mock.calls[0][0];
    const updatedSearch = callArg.search({ page: 3, pageSize: 20 });
    expect(updatedSearch.page).toBe(1);
    expect(updatedSearch.pageSize).toBe(50);
  });
});

describe('useServerPagination — antPagination adapter', () => {
  it('maps current/pageSize from hook state', () => {
    resetSearch({ page: '2', pageSize: '15' });
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.antPagination.current).toBe(2);
    expect(result.current.antPagination.pageSize).toBe(15);
  });

  it('shows size changer', () => {
    resetSearch();
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.antPagination.showSizeChanger).toBe(true);
  });

  it('showTotal returns Vietnamese formatted string', () => {
    resetSearch();
    const { result } = renderHook(() => useServerPagination());
    expect(result.current.antPagination.showTotal(100)).toBe('Tổng 100 bản ghi');
  });

  it('onChange calls navigate with new page and size', () => {
    resetSearch({ page: '1', pageSize: '20' });
    const { result } = renderHook(() => useServerPagination());
    mockNavigate.mockClear();

    act(() => {
      result.current.antPagination.onChange(3, 30);
    });

    expect(mockNavigate).toHaveBeenCalledOnce();
    const callArg = mockNavigate.mock.calls[0][0];
    const updatedSearch = callArg.search({ page: 1, pageSize: 20 });
    expect(updatedSearch.page).toBe(3);
    expect(updatedSearch.pageSize).toBe(30);
  });
});
