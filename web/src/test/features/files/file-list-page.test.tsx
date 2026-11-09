import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

const mockUseFiles = vi.fn(() => ({ data: { items: [] as FileRecordDto[], totalCount: 0 }, isLoading: false }));
const mockUseUploadFile = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));

const mockUseDeleteFile = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));

vi.mock('@/features/files/file-api', () => ({
  useFiles: () => mockUseFiles(),
  useUploadFile: () => mockUseUploadFile(),
  useDeleteFile: () => mockUseDeleteFile(),
  // file-list-page polls scan status for the lifecycle stepper; mock must mirror the module surface.
  useFileScanStatus: () => ({ data: undefined }),
  downloadFile: vi.fn(),
}));

// Sign-button eligibility input. Default = fully eligible (both enrollment kinds active+Approved)
// so the buttons render enabled and the pre-existing render assertions stay green; individual
// tests override per branch. Spread the real module so other consumers keep their exports.
const mockUseSignatureOverview = vi.fn(() => ({
  data: {
    hasApprovedEnrollment: true,
    enrollments: [
      { kind: 'UsbTokenVgca', approvalStatus: 'Approved', isActive: true },
      { kind: 'RemoteViettelMysign', approvalStatus: 'Approved', isActive: true },
    ],
  },
}));

vi.mock('@/features/profile/signatures/signatures-api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/profile/signatures/signatures-api')>()),
  useSignatureOverview: () => mockUseSignatureOverview(),
}));

// FileListPage also gates the two sign buttons on the deployment-wide upload-sign feature flag
// (feature:signing.upload-arbitrary-pdf, see file-signing-api.ts). Default the mock to ENABLED so
// these enrollment-eligibility tests can observe the buttons at all — otherwise the real hook's
// useQuery never resolves under jsdom (no MSW handler) and stays fail-closed (false), hiding both
// buttons entirely. Mirrors the mock already used in sign-uploaded-file-flow.test.tsx.
const mockUseUploadSignEnabled = vi.fn(() => true);

vi.mock('@/features/files/file-signing-api', () => ({
  useUploadSignEnabled: () => mockUseUploadSignEnabled(),
}));

import { FileListPage } from '@/features/files/file-list-page';
import type { FileRecordDto } from '@/features/files/file-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// A scan-passed PDF row — the only shape that renders the two sign buttons.
const PDF_ROW: FileRecordDto = {
  id: 'file-1',
  originalFileName: 'report.pdf',
  contentType: 'application/pdf',
  sizeBytes: 102400,
  virusScanStatus: 'Clean',
  uploadedBy: 'user-1',
  uploadedByName: 'Test User',
  uploadedAt: '2024-01-10T08:00:00Z',
} as FileRecordDto;

// antd renders icon-only buttons with the icon name as aria-label. Scope the match to <button>
// elements so the "Đã ký" status-column Tag (same safety-certificate icon, but a span) can't be
// mistaken for the sign button.
const findIconButton = (root: HTMLElement, iconLabel: string) =>
  Array.from(root.querySelectorAll('button')).find((b) =>
    b.querySelector(`[aria-label="${iconLabel}"]`),
  ) ?? null;
const usbButton = (root: HTMLElement) => findIconButton(root, 'safety-certificate');
const padesButton = (root: HTMLElement) => findIconButton(root, 'highlight');

describe('FileListPage — render', () => {
  beforeEach(() => {
    mockUseFiles.mockReturnValue({ data: { items: [], totalCount: 0 }, isLoading: false });
    mockUseSignatureOverview.mockReturnValue({
      data: {
        hasApprovedEnrollment: true,
        enrollments: [
          { kind: 'UsbTokenVgca', approvalStatus: 'Approved', isActive: true },
          { kind: 'RemoteViettelMysign', approvalStatus: 'Approved', isActive: true },
        ],
      },
    });
  });

  it('renders without crashing', () => {
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<FileListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.files.title')).toBeTruthy();
  });

  it('renders upload button', () => {
    render(<FileListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.files.upload')).toBeTruthy();
  });

  it('renders file table', () => {
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders file column headers', () => {
    render(<FileListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.files.col.fileName')).toBeTruthy();
    expect(screen.getByText('page.files.col.sizeBytes')).toBeTruthy();
    expect(screen.getByText('page.files.col.virusScan')).toBeTruthy();
  });

  it('renders file rows when data is provided', () => {
    // camelCase mirrors the real list response (System.Text.Json), same casing the table dataIndex reads.
    mockUseFiles.mockReturnValue({ data: { items: [PDF_ROW], totalCount: 1 }, isLoading: false });
    render(<FileListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('report.pdf')).toBeTruthy();
  });
});

describe('FileListPage — sign-button eligibility (enrollment-driven)', () => {
  beforeEach(() => {
    mockUseFiles.mockReturnValue({ data: { items: [PDF_ROW], totalCount: 1 }, isLoading: false });
    mockUseSignatureOverview.mockReturnValue({
      data: {
        hasApprovedEnrollment: true,
        enrollments: [
          { kind: 'UsbTokenVgca', approvalStatus: 'Approved', isActive: true },
          { kind: 'RemoteViettelMysign', approvalStatus: 'Approved', isActive: true },
        ],
      },
    });
  });

  it('both enabled when the user has active+Approved USB and Mysign enrollments', () => {
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(usbButton(container)?.disabled).toBe(false);
    expect(padesButton(container)?.disabled).toBe(false);
  });

  it('disables USB "Ký số" when there is no approved UsbTokenVgca enrollment', () => {
    // Only a Mysign enrollment — the USB path has no matching cert, so it stays disabled while
    // PAdES remains enabled. Enrollment kind, not tenant provider, drives each button.
    mockUseSignatureOverview.mockReturnValue({
      data: {
        hasApprovedEnrollment: true,
        enrollments: [{ kind: 'RemoteViettelMysign', approvalStatus: 'Approved', isActive: true }],
      },
    });
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(usbButton(container)?.disabled).toBe(true);
    expect(padesButton(container)?.disabled).toBe(false);
  });

  it('disables PAdES when there is no approved RemoteViettelMysign enrollment', () => {
    // Only a USB enrollment — remote PAdES would 403 at the binding guard, so it stays disabled
    // while USB remains enabled.
    mockUseSignatureOverview.mockReturnValue({
      data: {
        hasApprovedEnrollment: true,
        enrollments: [{ kind: 'UsbTokenVgca', approvalStatus: 'Approved', isActive: true }],
      },
    });
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(padesButton(container)?.disabled).toBe(true);
    expect(usbButton(container)?.disabled).toBe(false);
  });

  it('disables a button for a Pending (not yet approved) enrollment of its kind', () => {
    mockUseSignatureOverview.mockReturnValue({
      data: {
        hasApprovedEnrollment: false,
        enrollments: [
          { kind: 'UsbTokenVgca', approvalStatus: 'Pending', isActive: true },
          { kind: 'RemoteViettelMysign', approvalStatus: 'Pending', isActive: true },
        ],
      },
    });
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(usbButton(container)?.disabled).toBe(true);
    expect(padesButton(container)?.disabled).toBe(true);
  });

  it('disables a button for a revoked (Approved-but-inactive) enrollment — mirrors the BE guard', () => {
    // Revoke() clears IsActive but leaves ApprovalStatus=Approved; the server binds only active
    // rows, so neither path may unlock. The FE gate must require isActive too.
    mockUseSignatureOverview.mockReturnValue({
      data: {
        hasApprovedEnrollment: false,
        enrollments: [
          { kind: 'UsbTokenVgca', approvalStatus: 'Approved', isActive: false },
          { kind: 'RemoteViettelMysign', approvalStatus: 'Approved', isActive: false },
        ],
      },
    });
    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });
    expect(usbButton(container)?.disabled).toBe(true);
    expect(padesButton(container)?.disabled).toBe(true);
  });
});
