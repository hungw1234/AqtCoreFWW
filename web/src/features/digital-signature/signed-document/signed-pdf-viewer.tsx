/**
 * signed-pdf-viewer.tsx — Modal embedding a continuous-scroll react-pdf viewer for a signed PDF blob.
 *
 * Layout mirrors a full PDF viewer: a thumbnail sidebar + a continuous-scroll page list, with a
 * toolbar for zoom (incl. fit-width), a live page indicator, and in-document text search. Receives a
 * pre-fetched Blob and owns the objectURL lifecycle (revoked on unmount / blob change) so callers
 * never deal with URL bookkeeping.
 *
 * INVARIANT: The "Chưa xác minh chữ ký điện tử" caveat banner is ALWAYS rendered — a constant trust
 * signal, never conditional. Certificate verification is a future capability.
 *
 * Lazy-loaded by the steps panel so react-pdf + pdfjs only load when the viewer first opens.
 *
 * Scale note: all pages render eagerly (no virtualization). Signed documents are small (a few pages),
 * so this is simpler than windowing; revisit with react-window if very large PDFs become common.
 */

import { useState, useEffect, useRef, useCallback } from 'react';
import { Modal, Alert, Button, Space, Typography, Spin } from 'antd';
import { WarningOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Document, Page, pdfjs } from 'react-pdf';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import 'react-pdf/dist/Page/TextLayer.css';
import { SignedPdfToolbar } from './signed-pdf-toolbar';
import { SignedPdfThumbnails } from './signed-pdf-thumbnails';
import { usePdfTextSearch } from './use-pdf-text-search';

// Load the pdfjs worker from the same-origin static copy served at /vendor/pdfjs/
// (bundled by vite-plugin-static-copy from the pinned pdfjs-dist build — see vite.config.ts).
//
// This assignment MUST be unconditional: react-pdf eagerly initialises
// GlobalWorkerOptions.workerSrc to the bare specifier "pdf.worker.mjs" at its own module-load,
// before this module runs. That bare value is not transformed by Vite, so pdfjs tries to fetch it
// relative to the page, the dev server returns index.html (text/html), and the browser rejects it
// as a module-script MIME type. Guarding with `if (!workerSrc)` would see react-pdf's truthy
// default and skip — leaving the broken value in place. Always override.
pdfjs.GlobalWorkerOptions.workerSrc = '/vendor/pdfjs/pdf.worker.min.mjs';

const { Text } = Typography;

const ZOOM_STEP = 0.2;
const ZOOM_MIN = 0.4;
const ZOOM_MAX = 3;

// Highlight styling for search marks injected into react-pdf's (otherwise transparent) text layer.
const VIEWER_CSS = `
.signed-pdf-body { display: flex; gap: 8px; height: 68vh; }
.signed-pdf-sidebar { width: 132px; flex: none; overflow-y: auto; padding: 4px; background: #fafafa; border-radius: 6px; }
.signed-pdf-scroll { flex: 1; overflow: auto; background: #525659; border-radius: 6px; padding: 12px; text-align: center; }
.signed-pdf-page-wrap { display: inline-block; margin: 0 auto 12px; box-shadow: 0 1px 6px rgba(0,0,0,0.35); }
.signed-pdf-thumb { cursor: pointer; border: 2px solid transparent; border-radius: 4px; padding: 4px; margin-bottom: 6px; text-align: center; }
.signed-pdf-thumb.active { border-color: #1677ff; background: #e6f4ff; }
.signed-pdf-thumb-label { display: block; font-size: 11px; margin-top: 2px; }
.signed-pdf-scroll mark[data-pdf-mark] { background: #ffe58f; color: inherit; border-radius: 2px; }
.signed-pdf-scroll mark.pdf-mark-active { background: #ffa940; }
`;

export interface SignedPdfViewerProps {
  /** Open/close gate controlled by parent. */
  open: boolean;
  /** The signed PDF bytes — caller fetched with bearer token via fetchSignedStepBlob. */
  blob: Blob | null;
  /** Modal title (e.g. "Bước 1 – Xem tài liệu đã ký"). */
  title?: string;
  onClose: () => void;
}

export function SignedPdfViewer({ open, blob, title, onClose }: SignedPdfViewerProps) {
  const { t } = useTranslation();
  const [numPages, setNumPages] = useState(0);
  const [currentPage, setCurrentPage] = useState(1);
  const [scale, setScale] = useState(1);
  const [docError, setDocError] = useState<string | null>(null);
  const [objectUrl, setObjectUrl] = useState<string | null>(null);

  const objectUrlRef = useRef<string | null>(null);
  const pageRefs = useRef<Map<number, HTMLDivElement>>(new Map());
  const baseWidthRef = useRef<number>(0); // first page's native width — basis for fit-width
  const fittedRef = useRef<boolean>(false);

  const search = usePdfTextSearch();
  const scrollRef = search.containerRef; // single scroll container: search target + page-tracking root

  // objectURL is created from the blob and revoked on unmount / blob change to avoid memory leaks.
  useEffect(() => {
    if (objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current);
      objectUrlRef.current = null;
    }
    if (!blob) {
      setObjectUrl(null);
      return;
    }
    const url = URL.createObjectURL(blob);
    objectUrlRef.current = url;
    setObjectUrl(url);
    // Reset view state for the new document. This component is shared (Files + Forms) and its
    // contract permits swapping `blob` while `open` stays true, so per-document refs must be cleared
    // here — not just relied on antd's destroyOnHidden (which only resets on close) — otherwise a
    // smaller second document could observe stale page nodes or fit-width against the prior page width.
    setCurrentPage(1);
    setDocError(null);
    fittedRef.current = false;
    baseWidthRef.current = 0;
    pageRefs.current.clear();

    return () => {
      if (objectUrlRef.current) {
        URL.revokeObjectURL(objectUrlRef.current);
        objectUrlRef.current = null;
      }
    };
  }, [blob]);

  // Also revoke when the modal closes so the URL is not held while the viewer is hidden.
  useEffect(() => {
    if (!open && objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current);
      objectUrlRef.current = null;
      setObjectUrl(null);
    }
  }, [open]);

  const fitWidth = useCallback(() => {
    const root = scrollRef.current;
    if (!root || !baseWidthRef.current) return;
    const available = root.clientWidth - 24; // minus padding
    const next = available / baseWidthRef.current;
    setScale(Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, next)));
  }, [scrollRef]);

  // Track the most-visible page so the indicator + active thumbnail follow scroll position.
  useEffect(() => {
    const root = scrollRef.current;
    if (!root || numPages === 0) return;
    const observer = new IntersectionObserver(
      (entries) => {
        let best = -1;
        let bestRatio = 0;
        for (const entry of entries) {
          if (entry.isIntersecting && entry.intersectionRatio > bestRatio) {
            bestRatio = entry.intersectionRatio;
            best = Number((entry.target as HTMLElement).dataset.page);
          }
        }
        if (best > 0) setCurrentPage(best);
      },
      { root, threshold: [0.25, 0.5, 0.75] },
    );
    pageRefs.current.forEach((el) => observer.observe(el));
    return () => observer.disconnect();
  }, [numPages, objectUrl, scale, scrollRef]);

  function goToPage(page: number) {
    const el = pageRefs.current.get(page);
    if (el && typeof el.scrollIntoView === 'function') {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    setCurrentPage(page);
  }

  function handleDocumentLoad({ numPages: n }: { numPages: number }) {
    setNumPages(n);
    setDocError(null);
  }

  function handleDocumentError() {
    setDocError(t('digitalSignature.signedDocument.pdfLoadError', 'Không tải được tài liệu PDF'));
  }

  function handleFirstPageLoad(page: { originalWidth: number }) {
    baseWidthRef.current = page.originalWidth;
    if (!fittedRef.current) {
      fittedRef.current = true;
      fitWidth();
    }
  }

  const scalePercent = Math.round(scale * 100);

  return (
    <Modal
      open={open}
      title={title ?? t('digitalSignature.signedDocument.viewerTitle', 'Xem tài liệu đã ký')}
      onCancel={onClose}
      footer={
        <Button type="primary" onClick={onClose}>
          {t('common.close', 'Đóng')}
        </Button>
      }
      width={1080}
      destroyOnHidden
      styles={{ body: { padding: '12px 16px' } }}
    >
      <style>{VIEWER_CSS}</style>
      <Space direction="vertical" style={{ width: '100%' }} size="small">
        {/*
          MANDATORY caveat — always rendered regardless of state.
          Certificate equality (cert == signer) verification is a future capability.
          This banner must never be removed or made conditional.
        */}
        <Alert
          type="warning"
          showIcon
          icon={<WarningOutlined />}
          message={t(
            'digitalSignature.signedDocument.certNotVerifiedCaveat',
            'Chưa xác minh chữ ký điện tử — việc xác minh chứng thư số sẽ được hỗ trợ ở phiên bản sau.',
          )}
        />

        {!objectUrl && !docError && (
          <Space style={{ justifyContent: 'center', width: '100%', padding: '32px 0' }}>
            <Spin />
            <Text type="secondary">
              {t('digitalSignature.signedDocument.loading', 'Đang tải tài liệu...')}
            </Text>
          </Space>
        )}

        {docError && <Alert type="error" showIcon message={docError} />}

        {objectUrl && !docError && (
          <>
            <SignedPdfToolbar
              scalePercent={scalePercent}
              zoomMinReached={scale <= ZOOM_MIN}
              zoomMaxReached={scale >= ZOOM_MAX}
              onZoomIn={() => setScale((s) => Math.min(ZOOM_MAX, s + ZOOM_STEP))}
              onZoomOut={() => setScale((s) => Math.max(ZOOM_MIN, s - ZOOM_STEP))}
              onFitWidth={fitWidth}
              currentPage={currentPage}
              numPages={numPages}
              searchTerm={search.term}
              onSearchChange={search.setTerm}
              matchCount={search.matchCount}
              activeMatch={search.activeMatch}
              onNextMatch={search.next}
              onPrevMatch={search.prev}
            />

            <Document
              file={objectUrl}
              onLoadSuccess={handleDocumentLoad}
              onLoadError={handleDocumentError}
              loading={
                <Space style={{ justifyContent: 'center', width: '100%', padding: '32px 0' }}>
                  <Spin />
                  <Text type="secondary">
                    {t('digitalSignature.signedDocument.loading', 'Đang tải tài liệu...')}
                  </Text>
                </Space>
              }
            >
              <div className="signed-pdf-body">
                <div className="signed-pdf-sidebar">
                  <SignedPdfThumbnails
                    numPages={numPages}
                    currentPage={currentPage}
                    onSelect={goToPage}
                  />
                </div>

                <div className="signed-pdf-scroll" ref={scrollRef}>
                  {Array.from({ length: numPages }, (_, i) => i + 1).map((page) => (
                    <div
                      key={page}
                      data-page={page}
                      data-testid={`pdf-page-${page}`}
                      className="signed-pdf-page-wrap"
                      ref={(el) => {
                        if (el) pageRefs.current.set(page, el);
                        else pageRefs.current.delete(page);
                      }}
                    >
                      <Page
                        pageNumber={page}
                        scale={scale}
                        renderTextLayer
                        renderAnnotationLayer
                        customTextRenderer={search.customTextRenderer}
                        onLoadSuccess={page === 1 ? handleFirstPageLoad : undefined}
                      />
                    </div>
                  ))}
                </div>
              </div>
            </Document>
          </>
        )}
      </Space>
    </Modal>
  );
}
