/**
 * signature-placement-overlay.tsx — interactive picker for a visible-stamp rectangle on a PDF page.
 *
 * Renders one page at a time (react-pdf) at a fixed render width and lets the user draw / move / resize a
 * single rectangle over it. On every change it emits the rectangle converted to the BE contract
 * (PDF points, bottom-left origin) plus the 1-based page number — the coordinate math lives in the pure
 * placement-geometry module so this component only owns pointer plumbing + page navigation.
 *
 * Changing the page clears the rectangle (a placement is page-scoped) and emits null, so the parent never
 * sends a rect that belongs to a different page.
 *
 * The pdfjs worker is configured the same unconditional way as signed-pdf-viewer.tsx (see the note there).
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Space, Spin, Typography } from 'antd';
import { LeftOutlined, RightOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Document, Page, pdfjs } from 'react-pdf';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import 'react-pdf/dist/Page/TextLayer.css';
import { screenRectToPdfRect, type PdfRect, type ScreenRect } from './placement-geometry';

pdfjs.GlobalWorkerOptions.workerSrc = '/vendor/pdfjs/pdf.worker.min.mjs';

const { Text } = Typography;

/** Fixed render width (CSS px). The effective scale is derived from the page's intrinsic point width. */
const PAGE_RENDER_WIDTH = 520;
/** Minimum drawn size (px) below which a click is treated as a deselect, not a rectangle. */
const MIN_DRAW_PX = 8;
/** Size of the bottom-right resize handle (px). */
const HANDLE_PX = 14;

export interface PlacementSelection {
  /** 1-based page number the rectangle sits on. */
  page: number;
  /** Rectangle in PDF points, bottom-left origin. */
  rect: PdfRect;
}

export interface SignaturePlacementOverlayProps {
  /** Object URL of the PDF to place a stamp on (caller owns its lifecycle). */
  fileUrl: string | null;
  /** Emitted whenever the placement changes; null when there is no (valid) rectangle. */
  onChange: (selection: PlacementSelection | null) => void;
}

type Mode = 'idle' | 'draw' | 'move' | 'resize';

export function SignaturePlacementOverlay({ fileUrl, onChange }: SignaturePlacementOverlayProps) {
  const { t } = useTranslation();
  const [numPages, setNumPages] = useState(0);
  const [page, setPage] = useState(1);
  const [pageDims, setPageDims] = useState<{ widthPt: number; heightPt: number } | null>(null);
  const [rect, setRect] = useState<ScreenRect | null>(null);

  const surfaceRef = useRef<HTMLDivElement>(null);
  const modeRef = useRef<Mode>('idle');
  const anchorRef = useRef<{ x: number; y: number }>({ x: 0, y: 0 });
  const originRectRef = useRef<ScreenRect | null>(null);

  // Effective scale: renderedPx = pt * scale → scale = renderWidth / intrinsicWidthPt.
  const scale = pageDims ? PAGE_RENDER_WIDTH / pageDims.widthPt : 1;
  const renderHeight = pageDims ? PAGE_RENDER_WIDTH * (pageDims.heightPt / pageDims.widthPt) : 0;

  // Convert + emit on any change to the rect / page / dims.
  useEffect(() => {
    if (!rect || !pageDims) {
      onChange(null);
      return;
    }
    onChange({ page, rect: screenRectToPdfRect(rect, pageDims, scale) });
  }, [rect, pageDims, page, scale, onChange]);

  // A placement is page-scoped: switching pages drops the current rectangle.
  const goToPage = useCallback((next: number) => {
    setPage(next);
    setRect(null);
  }, []);

  function relativePoint(e: React.PointerEvent): { x: number; y: number } {
    const el = surfaceRef.current;
    const box = el?.getBoundingClientRect();
    const x = box ? e.clientX - box.left : e.clientX;
    const y = box ? e.clientY - box.top : e.clientY;
    return { x, y };
  }

  function onPointerDown(e: React.PointerEvent) {
    if (!pageDims) return;
    const p = relativePoint(e);
    surfaceRef.current?.setPointerCapture?.(e.pointerId);

    // Hit-test the existing rectangle: bottom-right handle → resize, body → move, else → draw new.
    if (rect) {
      const onHandle =
        p.x >= rect.left + rect.width - HANDLE_PX &&
        p.x <= rect.left + rect.width + 2 &&
        p.y >= rect.top + rect.height - HANDLE_PX &&
        p.y <= rect.top + rect.height + 2;
      const inside =
        p.x >= rect.left && p.x <= rect.left + rect.width && p.y >= rect.top && p.y <= rect.top + rect.height;
      if (onHandle) {
        modeRef.current = 'resize';
        anchorRef.current = p;
        originRectRef.current = rect;
        return;
      }
      if (inside) {
        modeRef.current = 'move';
        anchorRef.current = p;
        originRectRef.current = rect;
        return;
      }
    }
    modeRef.current = 'draw';
    anchorRef.current = p;
    setRect({ left: p.x, top: p.y, width: 0, height: 0 });
  }

  function onPointerMove(e: React.PointerEvent) {
    if (modeRef.current === 'idle') return;
    const p = relativePoint(e);
    const a = anchorRef.current;
    // Clamp in screen space too (not just at PDF-point conversion) so what the user sees matches what is sent (L1).
    const maxW = PAGE_RENDER_WIDTH;
    const maxH = renderHeight || PAGE_RENDER_WIDTH;
    const clamp = (v: number, max: number) => Math.max(0, Math.min(v, max));

    if (modeRef.current === 'draw') {
      const left = clamp(Math.min(a.x, p.x), maxW);
      const top = clamp(Math.min(a.y, p.y), maxH);
      setRect({
        left,
        top,
        width: Math.min(Math.abs(p.x - a.x), maxW - left),
        height: Math.min(Math.abs(p.y - a.y), maxH - top),
      });
    } else if (modeRef.current === 'move' && originRectRef.current) {
      const o = originRectRef.current;
      // Move keeps the size; clamp the position so the whole box stays on-page.
      setRect({
        ...o,
        left: clamp(o.left + (p.x - a.x), maxW - o.width),
        top: clamp(o.top + (p.y - a.y), maxH - o.height),
      });
    } else if (modeRef.current === 'resize' && originRectRef.current) {
      const o = originRectRef.current;
      setRect({
        ...o,
        width: clamp(p.x - o.left, maxW - o.left),
        height: clamp(p.y - o.top, maxH - o.top),
      });
    }
  }

  function onPointerUp(e: React.PointerEvent) {
    surfaceRef.current?.releasePointerCapture?.(e.pointerId);
    const finishedDrawing = modeRef.current === 'draw';
    modeRef.current = 'idle';
    originRectRef.current = null;
    // A too-small draw is a click-to-deselect, not a rectangle.
    if (finishedDrawing && rect && (rect.width < MIN_DRAW_PX || rect.height < MIN_DRAW_PX)) {
      setRect(null);
    }
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="small">
      <Space size="small">
        <Button
          size="small"
          icon={<LeftOutlined />}
          disabled={page <= 1}
          onClick={() => goToPage(page - 1)}
          aria-label={t('digitalSignature.placement.prevPage', 'Trang trước')}
        />
        <Text type="secondary">
          {t('digitalSignature.placement.pageIndicator', 'Trang {{page}}/{{total}}', { page, total: numPages || 1 })}
        </Text>
        <Button
          size="small"
          icon={<RightOutlined />}
          disabled={numPages > 0 && page >= numPages}
          onClick={() => goToPage(page + 1)}
          aria-label={t('digitalSignature.placement.nextPage', 'Trang sau')}
        />
      </Space>

      <Text type="secondary" style={{ fontSize: 12 }}>
        {t('digitalSignature.placement.drawHint', 'Kéo chuột trên trang để vẽ vị trí đóng dấu chữ ký; kéo cạnh dưới-phải để thay đổi kích thước.')}
      </Text>

      <div
        style={{
          position: 'relative',
          width: PAGE_RENDER_WIDTH,
          margin: '0 auto',
          background: '#f5f5f5',
          touchAction: 'none',
        }}
      >
        <Document
          file={fileUrl}
          loading={
            <Space style={{ justifyContent: 'center', width: '100%', padding: '48px 0' }}>
              <Spin />
            </Space>
          }
          onLoadSuccess={({ numPages: n }) => setNumPages(n)}
        >
          <Page
            pageNumber={page}
            width={PAGE_RENDER_WIDTH}
            renderTextLayer={false}
            renderAnnotationLayer={false}
            onLoadSuccess={(p: { originalWidth: number; originalHeight: number }) =>
              setPageDims({ widthPt: p.originalWidth, heightPt: p.originalHeight })
            }
          />
        </Document>

        {/* Transparent capture surface over the page — owns drawing/moving/resizing the rectangle. */}
        <div
          ref={surfaceRef}
          data-testid="placement-surface"
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          style={{
            position: 'absolute',
            inset: 0,
            height: renderHeight || '100%',
            cursor: 'crosshair',
          }}
        >
          {rect && (
            <div
              data-testid="placement-rect"
              style={{
                position: 'absolute',
                left: rect.left,
                top: rect.top,
                width: rect.width,
                height: rect.height,
                border: '2px solid #1677ff',
                background: 'rgba(22,119,255,0.15)',
                cursor: 'move',
              }}
            >
              <div
                style={{
                  position: 'absolute',
                  right: -2,
                  bottom: -2,
                  width: HANDLE_PX,
                  height: HANDLE_PX,
                  background: '#1677ff',
                  cursor: 'nwse-resize',
                }}
              />
            </div>
          )}
        </div>
      </div>
    </Space>
  );
}
