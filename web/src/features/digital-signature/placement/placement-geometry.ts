/**
 * placement-geometry.ts — pure coordinate conversion between the on-screen placement rectangle
 * (CSS px, top-left origin, relative to the rendered PDF page element) and the BE visible-stamp
 * contract (PDF points, BOTTOM-LEFT origin — see Identity SignaturePlacement.cs).
 *
 * react-pdf renders a page at an effective scale where renderedPx = pagePoint * scale. A screen rect
 * is converted back to points by dividing by that scale; the Y axis is flipped because PDF's origin
 * is the page's bottom-left corner while the DOM's is the top-left.
 *
 * Kept pure + framework-free so the coordinate math (this slice's #1 correctness risk) is unit-tested
 * in isolation from react-pdf / pointer-event plumbing.
 */

/** Visible-stamp slot. "initial" → paraph (profile.InitialSignatureKey); anything else → full signature. */
export type SignatureVisualType = 'full' | 'initial';

/** Rectangle in rendered CSS px, top-left origin, relative to the page element's content box. */
export interface ScreenRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** A PDF page's intrinsic size in points (react-pdf Page onLoadSuccess → originalWidth/originalHeight). */
export interface PageDimensionsPt {
  widthPt: number;
  heightPt: number;
}

/** Rectangle in PDF points, bottom-left origin — the SignaturePlacement contract. */
export interface PdfRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

/** Full placement payload serialized into CreateSigningRequestBody.placement (JSON string). */
export interface PlacementPayload {
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
  profilePublicId: string;
  visualType: SignatureVisualType;
}

function clamp(value: number, min: number, max: number): number {
  // A degenerate page (max < min) collapses to the lower bound rather than producing NaN.
  if (max < min) return min;
  return Math.min(Math.max(value, min), max);
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * Convert an on-screen rectangle to PDF-point coordinates (bottom-left origin), clamped to the page.
 *
 * @param rect   screen rect in rendered CSS px, relative to the page content box.
 * @param page   the page's intrinsic point dimensions.
 * @param scale  the react-pdf render scale (renderedPx = pt * scale). A non-positive scale falls back to 1.
 */
export function screenRectToPdfRect(rect: ScreenRect, page: PageDimensionsPt, scale: number): PdfRect {
  const s = scale > 0 ? scale : 1;
  // Size first, so the origin can be clamped against it (origin + size must stay on-page).
  const w = clamp(rect.width / s, 0, page.widthPt);
  const h = clamp(rect.height / s, 0, page.heightPt);
  const x = clamp(rect.left / s, 0, page.widthPt - w);
  // Flip Y: the screen measures from the top, PDF from the bottom. The rect's BOTTOM screen edge
  // (top + height) maps to the stamp's bottom-left Y in PDF space.
  const yFromTop = (rect.top + rect.height) / s;
  const y = clamp(page.heightPt - yFromTop, 0, page.heightPt - h);
  return { x: round2(x), y: round2(y), w: round2(w), h: round2(h) };
}

/** Build the placement JSON string for CreateSigningRequestBody.placement. */
export function buildPlacementJson(payload: PlacementPayload): string {
  return JSON.stringify({
    page: payload.page,
    x: payload.x,
    y: payload.y,
    w: payload.w,
    h: payload.h,
    profilePublicId: payload.profilePublicId,
    visualType: payload.visualType,
  });
}
