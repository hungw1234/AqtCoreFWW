// TC-FE-WF-KBNAV-001: initial state — focusedCell is null, getCellTabIndex falls back to (0,0)
// TC-FE-WF-KBNAV-002: ArrowDown — row increments, clamped at rowCount-1
// TC-FE-WF-KBNAV-003: ArrowUp — row decrements, clamped at 0
// TC-FE-WF-KBNAV-004: ArrowRight — col increments, clamped at colCount-1
// TC-FE-WF-KBNAV-005: ArrowLeft — col decrements, clamped at 0
// TC-FE-WF-KBNAV-006: Home / End — jump to col extremes in same row
// TC-FE-WF-KBNAV-007: Ctrl+Home / Ctrl+End — jump to grid corners
// TC-FE-WF-KBNAV-008: PageDown / PageUp — row jumps PAGE_SIZE rows, clamped
// TC-FE-WF-KBNAV-009: Space at col=0 — calls onToggleSelect; space at other col does nothing
// TC-FE-WF-KBNAV-010: scrollToIndex called on row changes, not on col-only changes
// TC-FE-WF-KBNAV-011: e.preventDefault() called for all handled navigation keys
// TC-FE-WF-KBNAV-012: isCellFocused returns correct boolean
// TC-FE-WF-KBNAV-013: setFocusedCell external setter works correctly

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useWorkflowGridKeyboardNav } from '@/features/workflow/use-workflow-grid-keyboard-nav';
import type { KeyboardEvent } from 'react';

// Helpers
function makeMockVirtualizer() {
  return { scrollToIndex: vi.fn() };
}

function makeEvent(key: string, ctrlKey = false): KeyboardEvent<HTMLDivElement> {
  return {
    key,
    ctrlKey,
    preventDefault: vi.fn(),
  } as unknown as KeyboardEvent<HTMLDivElement>;
}

const DEFAULT_OPTS = {
  rowCount: 10,
  colCount: 7,
} as const;

// ─── TC-FE-WF-KBNAV-001: initial state ────────────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-001: initial state', () => {
  it('focusedCell starts as null', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    expect(result.current.focusedCell).toBeNull();
  });

  it('getCellTabIndex returns 0 for (0,0) when focusedCell is null (roving tabIndex entry)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    expect(result.current.getCellTabIndex(0, 0)).toBe(0);
    expect(result.current.getCellTabIndex(0, 1)).toBe(-1);
    expect(result.current.getCellTabIndex(1, 0)).toBe(-1);
  });
});

// ─── TC-FE-WF-KBNAV-002: ArrowDown ────────────────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-002: ArrowDown', () => {
  let virtualizer: ReturnType<typeof makeMockVirtualizer>;
  let onToggleSelect: (rowIndex: number) => void;

  beforeEach(() => {
    virtualizer = makeMockVirtualizer();
    onToggleSelect = vi.fn<(rowIndex: number) => void>();
  });

  it('moves focus one row down from (0,0)', () => {
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 0, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowDown')));
    expect(result.current.focusedCell).toEqual({ row: 1, col: 0 });
  });

  it('clamps at rowCount-1 — does not go below last row', () => {
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ rowCount: 3, colCount: 7, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 2, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowDown')));
    expect(result.current.focusedCell).toEqual({ row: 2, col: 0 });
  });
});

// ─── TC-FE-WF-KBNAV-003: ArrowUp ──────────────────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-003: ArrowUp', () => {
  it('moves focus one row up from (2,0)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 2, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowUp')));
    expect(result.current.focusedCell).toEqual({ row: 1, col: 0 });
  });

  it('clamps at row 0 — does not go above first row', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 0, col: 3 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowUp')));
    expect(result.current.focusedCell).toEqual({ row: 0, col: 3 });
  });
});

// ─── TC-FE-WF-KBNAV-004/005: ArrowRight / ArrowLeft ───────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-004/005: ArrowRight/Left', () => {
  it('ArrowRight moves col right, clamped at colCount-1', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 1, col: 6 })); // colCount-1 = 6
    act(() => result.current.handleKeyDown(makeEvent('ArrowRight')));
    expect(result.current.focusedCell).toEqual({ row: 1, col: 6 }); // clamped
  });

  it('ArrowLeft moves col left, clamped at 0', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 1, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowLeft')));
    expect(result.current.focusedCell).toEqual({ row: 1, col: 0 }); // clamped
  });

  it('ArrowRight moves col when not at boundary', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 1, col: 3 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowRight')));
    expect(result.current.focusedCell).toEqual({ row: 1, col: 4 });
  });
});

// ─── TC-FE-WF-KBNAV-006: Home / End ──────────────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-006: Home/End', () => {
  it('Home jumps to col=0 in current row', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 3, col: 5 }));
    act(() => result.current.handleKeyDown(makeEvent('Home')));
    expect(result.current.focusedCell).toEqual({ row: 3, col: 0 });
  });

  it('End jumps to col=colCount-1 in current row', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 3, col: 1 }));
    act(() => result.current.handleKeyDown(makeEvent('End')));
    expect(result.current.focusedCell).toEqual({ row: 3, col: 6 }); // colCount-1 = 6
  });
});

// ─── TC-FE-WF-KBNAV-007: Ctrl+Home / Ctrl+End ────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-007: Ctrl+Home/End', () => {
  it('Ctrl+Home moves to (0,0) from anywhere', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 7, col: 5 }));
    act(() => result.current.handleKeyDown(makeEvent('Home', true))); // ctrlKey=true
    expect(result.current.focusedCell).toEqual({ row: 0, col: 0 });
  });

  it('Ctrl+End moves to (rowCount-1, colCount-1)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ rowCount: 10, colCount: 7, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 2, col: 1 }));
    act(() => result.current.handleKeyDown(makeEvent('End', true))); // ctrlKey=true
    expect(result.current.focusedCell).toEqual({ row: 9, col: 6 });
  });
});

// ─── TC-FE-WF-KBNAV-008: PageDown / PageUp ───────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-008: PageDown/PageUp', () => {
  it('PageDown advances row by 15 (PAGE_SIZE)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ rowCount: 50, colCount: 7, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 0, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('PageDown')));
    expect(result.current.focusedCell).toEqual({ row: 15, col: 0 });
  });

  it('PageDown clamps at rowCount-1 when near end', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ rowCount: 20, colCount: 7, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 18, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('PageDown')));
    expect(result.current.focusedCell).toEqual({ row: 19, col: 0 }); // rowCount-1 = 19
  });

  it('PageUp retreats row by 15 (PAGE_SIZE)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ rowCount: 50, colCount: 7, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 20, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('PageUp')));
    expect(result.current.focusedCell).toEqual({ row: 5, col: 0 });
  });

  it('PageUp clamps at row 0', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 3, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('PageUp')));
    expect(result.current.focusedCell).toEqual({ row: 0, col: 0 }); // clamped at 0
  });
});

// ─── TC-FE-WF-KBNAV-009: Space key ───────────────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-009: Space', () => {
  it('Space at col=0 calls onToggleSelect with current row index', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 4, col: 0 }));
    const ev = makeEvent(' '); // Space = literal ' '
    act(() => result.current.handleKeyDown(ev));
    expect(onToggleSelect).toHaveBeenCalledOnce();
    expect(onToggleSelect).toHaveBeenCalledWith(4);
    expect(ev.preventDefault).toHaveBeenCalled();
  });

  it('Space at col≠0 does NOT call onToggleSelect', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 4, col: 3 }));
    act(() => result.current.handleKeyDown(makeEvent(' ')));
    expect(onToggleSelect).not.toHaveBeenCalled();
  });
});

// ─── TC-FE-WF-KBNAV-010: scrollToIndex called on row changes only ─────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-010: scrollToIndex', () => {
  it('scrollToIndex called with new row after ArrowDown', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 2, col: 0 }));
    act(() => result.current.handleKeyDown(makeEvent('ArrowDown')));
    expect(virtualizer.scrollToIndex).toHaveBeenCalledWith(3, { align: 'auto' });
  });

  it('scrollToIndex NOT called on ArrowRight (col-only change)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 2, col: 0 }));
    virtualizer.scrollToIndex.mockClear();
    act(() => result.current.handleKeyDown(makeEvent('ArrowRight')));
    expect(virtualizer.scrollToIndex).not.toHaveBeenCalled();
  });
});

// ─── TC-FE-WF-KBNAV-011: preventDefault called for all navigation keys ────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-011: preventDefault', () => {
  const navKeys = ['ArrowDown', 'ArrowUp', 'ArrowRight', 'ArrowLeft', 'Home', 'End', 'PageDown', 'PageUp'];

  for (const key of navKeys) {
    it(`calls e.preventDefault() on ${key}`, () => {
      const virtualizer = makeMockVirtualizer();
      const onToggleSelect = vi.fn();
      const { result } = renderHook(() =>
        useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
      );
      act(() => result.current.setFocusedCell({ row: 2, col: 2 }));
      const ev = makeEvent(key);
      act(() => result.current.handleKeyDown(ev));
      expect(ev.preventDefault).toHaveBeenCalled();
    });
  }
});

// ─── TC-FE-WF-KBNAV-011b: null-state first keypress fallback ─────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-011b: null-state keypress', () => {
  it('ArrowDown from null focusedCell uses (0,0) as origin — produces row=1,col=0', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    // focusedCell is null — first keypress should use (0,0) fallback
    act(() => result.current.handleKeyDown(makeEvent('ArrowDown')));
    expect(result.current.focusedCell).toEqual({ row: 1, col: 0 });
  });

  it('Space at col=0 from null focusedCell calls onToggleSelect(0)', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.handleKeyDown(makeEvent(' ')));
    expect(onToggleSelect).toHaveBeenCalledWith(0);
  });

  it('Space at col≠0 still calls e.preventDefault()', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 2, col: 3 }));
    const ev = makeEvent(' ');
    act(() => result.current.handleKeyDown(ev));
    expect(ev.preventDefault).toHaveBeenCalled();
    expect(onToggleSelect).not.toHaveBeenCalled();
  });
});

// ─── TC-FE-WF-KBNAV-012: isCellFocused ───────────────────────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-012: isCellFocused', () => {
  it('returns true for focused cell, false for others', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 3, col: 2 }));
    expect(result.current.isCellFocused(3, 2)).toBe(true);
    expect(result.current.isCellFocused(3, 1)).toBe(false);
    expect(result.current.isCellFocused(2, 2)).toBe(false);
  });

  it('returns false for all cells when focusedCell is null', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    // Initial state — null
    expect(result.current.isCellFocused(0, 0)).toBe(false);
    expect(result.current.isCellFocused(1, 1)).toBe(false);
  });
});

// ─── TC-FE-WF-KBNAV-013: setFocusedCell external setter ──────────────────────
describe('useWorkflowGridKeyboardNav — TC-FE-WF-KBNAV-013: setFocusedCell', () => {
  it('external setFocusedCell updates focusedCell and getCellTabIndex', () => {
    const virtualizer = makeMockVirtualizer();
    const onToggleSelect = vi.fn();
    const { result } = renderHook(() =>
      useWorkflowGridKeyboardNav({ ...DEFAULT_OPTS, virtualizer, onToggleSelect }),
    );
    act(() => result.current.setFocusedCell({ row: 5, col: 3 }));
    expect(result.current.focusedCell).toEqual({ row: 5, col: 3 });
    expect(result.current.getCellTabIndex(5, 3)).toBe(0);
    expect(result.current.getCellTabIndex(0, 0)).toBe(-1); // no longer the default
  });
});
