// WAI-ARIA 1.2 grid keyboard navigation hook — roving tabIndex + virtualizer scroll sync.
// Handles 11 bindings: ↑↓←→ Home End Ctrl+Home Ctrl+End PageUp PageDown Space.
// Pure hook — no DOM .focus() calls (wired in Phase 03).
import { useState, useCallback } from 'react';
import type { KeyboardEvent } from 'react';

const PAGE_SIZE = 15 as const;

interface GridVirtualizer {
  scrollToIndex: (index: number, opts?: { align: 'auto' | 'start' | 'end' | 'center' }) => void;
}

export interface UseWorkflowGridKeyboardNavOptions {
  rowCount: number;
  colCount: number; // 7 cols: checkbox | entityType | entityId | state | started | due | status
  virtualizer: GridVirtualizer;
  onToggleSelect: (rowIndex: number) => void; // called when Space pressed at col=0
}

export interface UseWorkflowGridKeyboardNavResult {
  focusedCell: { row: number; col: number } | null;
  setFocusedCell: React.Dispatch<React.SetStateAction<{ row: number; col: number } | null>>;
  handleKeyDown: (e: KeyboardEvent<HTMLDivElement>) => void;
  getCellTabIndex: (row: number, col: number) => 0 | -1;
  isCellFocused: (row: number, col: number) => boolean;
}

export function useWorkflowGridKeyboardNav({
  rowCount, colCount, virtualizer, onToggleSelect,
}: UseWorkflowGridKeyboardNavOptions): UseWorkflowGridKeyboardNavResult {
  const [focusedCell, setFocusedCell] = useState<{ row: number; col: number } | null>(null);

  const handleKeyDown = useCallback((e: KeyboardEvent<HTMLDivElement>) => {
    const clr = (r: number) => Math.max(0, Math.min(rowCount - 1, r));
    const clc = (c: number) => Math.max(0, Math.min(colCount - 1, c));
    const cur = focusedCell ?? { row: 0, col: 0 };
    let { row, col } = cur;

    switch (e.key) {
      case 'ArrowDown':  e.preventDefault(); row = clr(cur.row + 1); break;
      case 'ArrowUp':    e.preventDefault(); row = clr(cur.row - 1); break;
      case 'ArrowRight': e.preventDefault(); col = clc(cur.col + 1); break;
      case 'ArrowLeft':  e.preventDefault(); col = clc(cur.col - 1); break;
      case 'Home':
        e.preventDefault();
        if (e.ctrlKey) { row = 0; col = 0; } else { col = 0; }
        break;
      case 'End':
        e.preventDefault();
        if (e.ctrlKey) { row = clr(rowCount - 1); col = clc(colCount - 1); } else { col = clc(colCount - 1); }
        break;
      case 'PageDown': e.preventDefault(); row = clr(cur.row + PAGE_SIZE); break;
      case 'PageUp':   e.preventDefault(); row = clr(cur.row - PAGE_SIZE); break;
      case ' ':
        e.preventDefault();
        if (cur.col === 0) onToggleSelect(cur.row);
        return; // no focus movement on Space
      default:
        return; // unhandled key — no state update
    }

    if (row !== cur.row) virtualizer.scrollToIndex(row, { align: 'auto' });
    setFocusedCell({ row, col });
  }, [focusedCell, rowCount, colCount, virtualizer, onToggleSelect]);

  const getCellTabIndex = useCallback(
    (row: number, col: number): 0 | -1 =>
      (focusedCell === null ? row === 0 && col === 0 : focusedCell.row === row && focusedCell.col === col) ? 0 : -1,
    [focusedCell],
  );

  const isCellFocused = useCallback(
    (row: number, col: number): boolean =>
      focusedCell !== null && focusedCell.row === row && focusedCell.col === col,
    [focusedCell],
  );

  return { focusedCell, setFocusedCell, handleKeyDown, getCellTabIndex, isCellFocused };
}
