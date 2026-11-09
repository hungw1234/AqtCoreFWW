// placement-geometry — pure coordinate-conversion unit tests (Phase 6 Slice D).
// Pins the screen-px → PDF-point contract: scale division, bottom-left Y-flip, on-page clamping,
// and the placement JSON shape the BE SignaturePlacement parser binds (case-insensitive {page,x,y,w,h,...}).

import { describe, it, expect } from 'vitest';
import {
  screenRectToPdfRect,
  buildPlacementJson,
  type PageDimensionsPt,
} from '../placement-geometry';

// A4 portrait in points.
const A4: PageDimensionsPt = { widthPt: 595, heightPt: 842 };

describe('screenRectToPdfRect', () => {
  it('flips Y to bottom-left origin at scale 1', () => {
    const rect = screenRectToPdfRect({ left: 100, top: 100, width: 200, height: 50 }, A4, 1);
    // x/w/h pass through 1:1; y = pageHeight - (top + height) = 842 - 150 = 692.
    expect(rect).toEqual({ x: 100, y: 692, w: 200, h: 50 });
  });

  it('divides by scale so a 2x-rendered rect yields the same points', () => {
    const at1 = screenRectToPdfRect({ left: 100, top: 100, width: 200, height: 50 }, A4, 1);
    const at2 = screenRectToPdfRect({ left: 200, top: 200, width: 400, height: 100 }, A4, 2);
    expect(at2).toEqual(at1);
  });

  it('clamps the origin so origin + size never leaves the page', () => {
    // A wide rect near the right edge: width keeps its value but x is pulled back to widthPt - w.
    const rect = screenRectToPdfRect({ left: 580, top: 10, width: 100, height: 30 }, A4, 1);
    expect(rect.w).toBe(100);
    expect(rect.x).toBe(595 - 100); // 495, not 580
    expect(rect.x + rect.w).toBeLessThanOrEqual(A4.widthPt);
  });

  it('clamps width/height to the page bounds', () => {
    const rect = screenRectToPdfRect({ left: 0, top: 0, width: 9999, height: 9999 }, A4, 1);
    expect(rect.w).toBe(595);
    expect(rect.h).toBe(842);
  });

  it('falls back to scale 1 for a non-positive scale instead of dividing by zero', () => {
    const rect = screenRectToPdfRect({ left: 10, top: 10, width: 20, height: 20 }, A4, 0);
    expect(Number.isFinite(rect.x)).toBe(true);
    expect(rect).toEqual({ x: 10, y: 842 - 30, w: 20, h: 20 });
  });

  it('rounds to 2 decimals', () => {
    const rect = screenRectToPdfRect({ left: 33.333, top: 0, width: 66.666, height: 10 }, A4, 3);
    // 33.333/3 = 11.111 → 11.11 ; 66.666/3 = 22.222 → 22.22
    expect(rect.x).toBe(11.11);
    expect(rect.w).toBe(22.22);
  });
});

describe('buildPlacementJson', () => {
  it('serializes the full payload with the BE field names', () => {
    const json = buildPlacementJson({
      page: 2,
      x: 100,
      y: 692,
      w: 200,
      h: 50,
      profilePublicId: '11111111-1111-1111-1111-111111111111',
      visualType: 'initial',
    });
    expect(JSON.parse(json)).toEqual({
      page: 2,
      x: 100,
      y: 692,
      w: 200,
      h: 50,
      profilePublicId: '11111111-1111-1111-1111-111111111111',
      visualType: 'initial',
    });
  });
});
