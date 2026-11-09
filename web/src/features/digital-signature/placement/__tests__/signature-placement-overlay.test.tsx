// SignaturePlacementOverlay — Phase 6 Slice D.
// react-pdf is mocked (no pdfjs in jsdom). Verifies: a drag draws a rectangle and emits a page-scoped
// PDF-point selection; changing pages clears the selection (emits null).

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, createEvent } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key }),
}));

// Mock react-pdf: Document reports 3 pages, Page reports A4 intrinsic point dims — both via mount effects.
vi.mock('react-pdf', async () => {
  const React = await import('react');
  // Fire onLoadSuccess exactly once (ref-guard, no dep array). The component passes a fresh inline
  // onLoadSuccess each render — real react-pdf fires it once per load, not per prop identity, so a
  // dep-tracked effect here would loop setState→render→setState. The guard mirrors real behaviour.
  const Document = ({ children, onLoadSuccess }: { children?: React.ReactNode; onLoadSuccess?: (p: { numPages: number }) => void }) => {
    const fired = React.useRef(false);
    React.useEffect(() => { if (!fired.current) { fired.current = true; onLoadSuccess?.({ numPages: 3 }); } });
    return React.createElement('div', { 'data-testid': 'doc' }, children);
  };
  const Page = ({ onLoadSuccess }: { onLoadSuccess?: (p: { originalWidth: number; originalHeight: number }) => void }) => {
    const fired = React.useRef(false);
    React.useEffect(() => { if (!fired.current) { fired.current = true; onLoadSuccess?.({ originalWidth: 595, originalHeight: 842 }); } });
    return React.createElement('div', { 'data-testid': 'page' });
  };
  return { pdfjs: { GlobalWorkerOptions: {} }, Document, Page };
});

import { SignaturePlacementOverlay } from '../signature-placement-overlay';

beforeEach(() => {
  // jsdom returns an all-zero rect; pin the surface origin at (0,0) so client coords map 1:1 to local px.
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    left: 0, top: 0, right: 520, bottom: 736, width: 520, height: 736, x: 0, y: 0, toJSON: () => ({}),
  } as DOMRect);
});

// jsdom's PointerEvent ignores clientX/clientY from the init dict, so define them on the event explicitly.
function firePointer(el: Element, type: 'pointerDown' | 'pointerMove' | 'pointerUp', x: number, y: number) {
  const ev = createEvent[type](el, { pointerId: 1 });
  Object.defineProperty(ev, 'clientX', { value: x });
  Object.defineProperty(ev, 'clientY', { value: y });
  fireEvent(el, ev);
}

function drawRect() {
  const surface = screen.getByTestId('placement-surface');
  firePointer(surface, 'pointerDown', 100, 100);
  firePointer(surface, 'pointerMove', 300, 150);
  firePointer(surface, 'pointerUp', 300, 150);
}

describe('SignaturePlacementOverlay', () => {
  it('emits a page-scoped PDF-point selection after a drag', () => {
    const onChange = vi.fn();
    render(<SignaturePlacementOverlay fileUrl="blob:fake" onChange={onChange} />);

    drawRect();

    const last = onChange.mock.calls.at(-1)?.[0];
    expect(last).not.toBeNull();
    expect(last.page).toBe(1);
    // 200px wide / (520/595 scale) ≈ 228.8pt ; bottom-left Y-flip keeps it on-page.
    expect(last.rect.w).toBeGreaterThan(0);
    expect(last.rect.h).toBeGreaterThan(0);
    expect(last.rect.x).toBeGreaterThanOrEqual(0);
    expect(last.rect.y).toBeGreaterThanOrEqual(0);
  });

  it('clears the selection (emits null) when navigating to another page', () => {
    const onChange = vi.fn();
    render(<SignaturePlacementOverlay fileUrl="blob:fake" onChange={onChange} />);
    drawRect();
    onChange.mockClear();

    fireEvent.click(screen.getByLabelText('Trang sau'));

    expect(onChange).toHaveBeenLastCalledWith(null);
  });
});
