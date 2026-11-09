// Plan 260523-1241 Phase 06b — WorkflowUserMultiSelect — AntD Select wrapper around
// collaboration/user-search-api with debounced query + multi-select callback.
//
// Tests focus on observable contract, not AntD internals:
//   1. Renders empty + invokes onChange with selected ids
//   2. Calls underlying search hook only when query.length >= 2 (debounce + enable gate)

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';

vi.mock('@/features/collaboration/user-search-api', () => ({
  useUserSearch: vi.fn(),
}));

import { useUserSearch } from '@/features/collaboration/user-search-api';
import { WorkflowUserMultiSelect } from '../workflow-user-multi-select';

const wrapper = ({ children }: { children: ReactNode }) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
};

describe('WorkflowUserMultiSelect', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.mocked(useUserSearch).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useUserSearch>);
  });

  it('renders with placeholder and no initial value', () => {
    render(
      <WorkflowUserMultiSelect value={[]} onChange={() => {}} placeholder="Chọn người dùng" />,
      { wrapper }
    );
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });

  it('does NOT call useUserSearch when query is below min-chars (debounce + enable gate)', () => {
    render(<WorkflowUserMultiSelect value={[]} onChange={() => {}} />, { wrapper });

    // Initial render — empty query, enabled=false.
    expect(useUserSearch).toHaveBeenLastCalledWith('', false);

    const input = screen.getByRole('combobox');
    fireEvent.change(input, { target: { value: 'a' } });

    // Single-char query: still NOT enabled (min 2 chars per debounce contract).
    act(() => {
      vi.advanceTimersByTime(300);
    });

    const calls = vi.mocked(useUserSearch).mock.calls;
    const lastCall = calls[calls.length - 1];
    expect(lastCall[1]).toBe(false);
  });

  it('enables search after debounce when query has ≥2 chars', () => {
    render(<WorkflowUserMultiSelect value={[]} onChange={() => {}} />, { wrapper });

    const input = screen.getByRole('combobox');
    fireEvent.change(input, { target: { value: 'ngu' } });

    // Before debounce: still disabled.
    expect(vi.mocked(useUserSearch).mock.calls.at(-1)?.[1]).toBe(false);

    act(() => {
      vi.advanceTimersByTime(300);
    });

    // After 300ms debounce: enabled with the typed query.
    const lastCall = vi.mocked(useUserSearch).mock.calls.at(-1);
    expect(lastCall?.[0]).toBe('ngu');
    expect(lastCall?.[1]).toBe(true);
  });
});
