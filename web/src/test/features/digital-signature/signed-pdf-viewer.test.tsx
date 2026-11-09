/**
 * signed-pdf-viewer.test.tsx
 *
 * Covers:
 *   - Caveat banner "Chưa xác minh chữ ký điện tử" ALWAYS rendered (open=true).
 *   - Document renders with the objectURL created from the blob.
 *   - Continuous scroll renders every page; thumbnail sidebar renders one entry per page.
 *   - Zoom + search controls render.
 *   - Close button invokes onClose.
 *   - objectURL is revoked on unmount (memory-leak guard).
 *   - pdfjs worker is configured to the same-origin /vendor path, overriding react-pdf's default.
 *
 * jsdom cannot run pdfjs workers, canvas rendering, or IntersectionObserver, so react-pdf is mocked
 * at the module level and IntersectionObserver is stubbed — the PDF raster is irrelevant for
 * UI-chrome assertions.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── i18n stub ────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, fallback?: string) =>
      typeof fallback === 'string' ? fallback : key,
    i18n: { language: 'vi' },
  }),
}));

// ─── react-pdf mock ───────────────────────────────────────────────────────────
//
// jsdom cannot run pdfjs workers or canvas rendering. Mock react-pdf's Document, Page and Thumbnail
// at the import boundary so the viewer chrome (caveat, toolbar, thumbnails, close) can be tested
// without rasterisation. Document calls onLoadSuccess synchronously with 3 pages so the continuous
// page list + thumbnail sidebar render.

vi.mock('react-pdf', () => {
  const Document = ({
    file,
    onLoadSuccess,
    children,
  }: {
    file: string;
    onLoadSuccess?: (result: { numPages: number }) => void;
    children?: ReactNode;
    loading?: ReactNode;
  }) => {
    if (onLoadSuccess && file) {
      onLoadSuccess({ numPages: 3 });
    }
    return createElement('div', { 'data-testid': 'pdf-document' }, children);
  };

  const Page = ({ pageNumber }: { pageNumber: number }) =>
    createElement('div', { 'data-testid': `pdf-page-canvas-${pageNumber}` });

  const Thumbnail = ({ pageNumber }: { pageNumber: number }) =>
    createElement('div', { 'data-testid': `pdf-thumb-canvas-${pageNumber}` });

  return {
    Document,
    Page,
    Thumbnail,
    // Mirror react-pdf's real behaviour: it eagerly seeds workerSrc with the bare specifier
    // "pdf.worker.mjs" at module-load. The viewer MUST override this broken default, so the mock
    // must reproduce it — seeding '' here would let a guarded (if-falsy) assignment pass the test
    // while still failing in production.
    pdfjs: {
      GlobalWorkerOptions: { workerSrc: 'pdf.worker.mjs' },
    },
  };
});

// ─── CSS imports mock — react-pdf imports annotation/text layer CSS ────────────

vi.mock('react-pdf/dist/Page/AnnotationLayer.css', () => ({}));
vi.mock('react-pdf/dist/Page/TextLayer.css', () => ({}));

// ─── URL API stub (jsdom does not fully implement createObjectURL) ─────────────

const mockCreateObjectURL = vi.fn(() => 'blob:mock-object-url');
const mockRevokeObjectURL = vi.fn();

// ─── Import component under test ──────────────────────────────────────────────

import { SignedPdfViewer } from '@/features/digital-signature/signed-document/signed-pdf-viewer';
// Importing the component above runs its module-load worker configuration against the mocked pdfjs.
import { pdfjs } from 'react-pdf';

// ─── Helpers ──────────────────────────────────────────────────────────────────

const PDF_BLOB = new Blob(['%PDF-1.4'], { type: 'application/pdf' });

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ─── Setup / teardown ─────────────────────────────────────────────────────────

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal('URL', {
    createObjectURL: mockCreateObjectURL,
    revokeObjectURL: mockRevokeObjectURL,
  });
  // jsdom has no IntersectionObserver — the viewer uses it for scroll-position page tracking.
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('SignedPdfViewer — caveat banner (INVARIANT)', () => {
  it('always renders the cert-not-verified caveat when open=true', async () => {
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    await waitFor(() =>
      expect(screen.getByText(/Chưa xác minh chữ ký điện tử/)).toBeTruthy(),
    );
  });

  it('renders the caveat even when blob is null (loading state)', () => {
    render(<SignedPdfViewer open blob={null} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    expect(screen.getByText(/Chưa xác minh chữ ký điện tử/)).toBeTruthy();
  });
});

describe('SignedPdfViewer — document rendering', () => {
  it('creates an objectURL from the blob and passes it to Document', async () => {
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(mockCreateObjectURL).toHaveBeenCalledWith(PDF_BLOB));
    expect(screen.getByTestId('pdf-document')).toBeTruthy();
  });

  it('renders every page continuously (no prev/next paging)', async () => {
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    // numPages=3 from the mock → all three page canvases are mounted at once.
    await waitFor(() => expect(screen.getByTestId('pdf-page-canvas-1')).toBeTruthy());
    expect(screen.getByTestId('pdf-page-canvas-2')).toBeTruthy();
    expect(screen.getByTestId('pdf-page-canvas-3')).toBeTruthy();
  });

  it('renders a thumbnail entry per page in the sidebar', async () => {
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(screen.getByTestId('pdf-thumb-1')).toBeTruthy());
    expect(screen.getByTestId('pdf-thumb-2')).toBeTruthy();
    expect(screen.getByTestId('pdf-thumb-3')).toBeTruthy();
  });

  it('renders zoom indicator and search input', async () => {
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    // Zoom defaults to 100% (no page dims in mock → fit-width no-ops).
    await waitFor(() => expect(screen.getByText('100%')).toBeTruthy());
    expect(screen.getByPlaceholderText(/Tìm trong tài liệu/)).toBeTruthy();
  });

  it('navigates to a page when its thumbnail is clicked (no scroll error in jsdom)', async () => {
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(screen.getByTestId('pdf-thumb-2')).toBeTruthy());
    // goToPage guards scrollIntoView (absent in jsdom); clicking must not throw.
    fireEvent.click(screen.getByTestId('pdf-thumb-2'));
    expect(screen.getByTestId('pdf-page-canvas-2')).toBeTruthy();
  });
});

describe('SignedPdfViewer — close and cleanup', () => {
  it('calls onClose when the close button is clicked', async () => {
    const onClose = vi.fn();
    render(<SignedPdfViewer open blob={PDF_BLOB} onClose={onClose} />, { wrapper: makeWrapper() });

    await waitFor(() => expect(screen.getByRole('button', { name: /Đóng/ })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /Đóng/ }));

    expect(onClose).toHaveBeenCalledOnce();
  });

  it('revokes the objectURL when blob changes to null (unmount guard)', async () => {
    const { rerender } = render(
      <SignedPdfViewer open blob={PDF_BLOB} onClose={vi.fn()} />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(mockCreateObjectURL).toHaveBeenCalledOnce());

    await act(async () => {
      rerender(
        createElement(makeWrapper(), {
          children: createElement(SignedPdfViewer, { open: true, blob: null, onClose: vi.fn() }),
        }),
      );
    });

    expect(mockRevokeObjectURL).toHaveBeenCalledWith('blob:mock-object-url');
  });
});

describe('SignedPdfViewer — worker configuration (regression guard)', () => {
  // react-pdf seeds GlobalWorkerOptions.workerSrc with the bare specifier "pdf.worker.mjs" at its
  // own module-load (the mock above reproduces this). That bare value is not transformed by Vite,
  // so the dev server serves index.html (text/html) for the worker and the browser rejects it as a
  // module-script MIME type. The viewer MUST unconditionally override it with the fixed same-origin
  // path served by vite-plugin-static-copy. Because the mock seeds the broken default, this assertion
  // fails if the override is ever re-guarded with `if (!workerSrc)`.
  it('overrides react-pdf default with the same-origin /vendor/pdfjs worker path', () => {
    expect(pdfjs.GlobalWorkerOptions.workerSrc).toBe('/vendor/pdfjs/pdf.worker.min.mjs');
  });
});
