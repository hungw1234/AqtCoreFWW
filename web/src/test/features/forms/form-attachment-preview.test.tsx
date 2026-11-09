import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { FileMetadataDto, FileStatus } from '@/features/files/file-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (_key: string, opts?: { defaultValue?: string }) => opts?.defaultValue ?? _key,
  }),
}));

// The component reads scan status + metadata via useFileScanStatus and (for images) bytes via
// useFileBlob; both are mocked so each test drives a specific FileStatus deterministically.
const mockScanData = vi.fn<() => { data: FileMetadataDto | undefined; isLoading: boolean }>();
const mockDownloadFile = vi.fn();

vi.mock('@/features/files/file-api', () => ({
  useFileScanStatus: () => mockScanData(),
  useFileBlob: () => ({ data: undefined }),
  downloadFile: (...args: unknown[]) => mockDownloadFile(...args),
}));

import { AttachmentPreview } from '@/features/forms/form-attachment-preview';

function meta(status: FileStatus, overrides: Partial<FileMetadataDto> = {}): FileMetadataDto {
  return {
    id: 'f1',
    originalFileName: 'doc.txt',
    contentType: 'text/plain',
    sizeBytes: 10,
    status,
    createdAt: '2026-06-14T00:00:00Z',
    ...overrides,
  };
}

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('AttachmentPreview — scan-status gate', () => {
  beforeEach(() => {
    mockScanData.mockReset();
    mockDownloadFile.mockReset();
  });

  it('Quarantined: shows scanning indicator and offers no download', () => {
    mockScanData.mockReturnValue({ data: meta('Quarantined'), isLoading: false });

    render(<AttachmentPreview fileId="f1" />, { wrapper: makeWrapper() });

    expect(screen.getByText('Đang quét virus…')).toBeTruthy();
    fireEvent.click(screen.getByText('doc.txt'));
    expect(mockDownloadFile).not.toHaveBeenCalled();
  });

  it('Rejected: shows rejected tag and offers no download', () => {
    mockScanData.mockReturnValue({ data: meta('Rejected'), isLoading: false });

    render(<AttachmentPreview fileId="f1" />, { wrapper: makeWrapper() });

    expect(screen.getByText('Tệp bị từ chối (nhiễm virus)')).toBeTruthy();
    fireEvent.click(screen.getByText('doc.txt'));
    expect(mockDownloadFile).not.toHaveBeenCalled();
  });

  it('Available: renders a download link that fetches the file on click', () => {
    mockScanData.mockReturnValue({ data: meta('Available'), isLoading: false });

    render(<AttachmentPreview fileId="f1" />, { wrapper: makeWrapper() });

    expect(screen.queryByText('Đang quét virus…')).toBeNull();
    expect(screen.queryByText('Tệp bị từ chối (nhiễm virus)')).toBeNull();
    fireEvent.click(screen.getByText('doc.txt'));
    expect(mockDownloadFile).toHaveBeenCalledWith('f1', 'doc.txt');
  });

  it('shows a spinner while metadata is loading', () => {
    mockScanData.mockReturnValue({ data: undefined, isLoading: true });

    const { container } = render(<AttachmentPreview fileId="f1" />, { wrapper: makeWrapper() });

    expect(container.querySelector('.ant-spin')).toBeTruthy();
  });
});
