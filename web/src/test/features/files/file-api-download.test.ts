import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// Mock the transport + toast surfaces the download helpers depend on.
const mockGet = vi.fn();
vi.mock('@/core/api', () => ({
  apiClient: { get: (...args: unknown[]) => mockGet(...args) },
}));
const mockError = vi.fn();
vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { error: (...args: unknown[]) => mockError(...args) },
}));

import { fetchFileBlob, downloadFile } from '@/features/files/file-api';

describe('file download transport', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // jsdom has no object-URL impl — stub so the helper can run.
    URL.createObjectURL = vi.fn(() => 'blob:mock-url');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetchFileBlob hits the bare /files/{id} stream route with responseType blob (no /download suffix)', async () => {
    const blob = new Blob(['x']);
    mockGet.mockResolvedValueOnce({ data: blob });

    const result = await fetchFileBlob('abc');

    expect(mockGet).toHaveBeenCalledWith('/files/abc', { responseType: 'blob' });
    expect(result).toBe(blob);
  });

  it('downloadFile saves the authenticated blob via a transient object URL and revokes it', async () => {
    const blob = new Blob(['data']);
    mockGet.mockResolvedValueOnce({ data: blob });
    const clickSpy = vi.fn();
    const anchor = { href: '', download: '', click: clickSpy, remove: vi.fn() } as unknown as HTMLAnchorElement;
    const createSpy = vi.spyOn(document, 'createElement').mockReturnValue(anchor);
    vi.spyOn(document.body, 'appendChild').mockImplementation((n) => n);

    await downloadFile('file-1', 'report.pdf');

    expect(URL.createObjectURL).toHaveBeenCalledWith(blob);
    expect(anchor.href).toBe('blob:mock-url');
    expect(anchor.download).toBe('report.pdf');
    expect(clickSpy).toHaveBeenCalledOnce();
    // Revoke after click so the blob isn't leaked.
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
    expect(mockError).not.toHaveBeenCalled();
    createSpy.mockRestore();
  });

  it('downloadFile surfaces a toast (not an unhandled rejection) when the fetch fails', async () => {
    mockGet.mockRejectedValueOnce(new Error('401'));

    await downloadFile('file-1', 'report.pdf');

    expect(mockError).toHaveBeenCalledOnce();
    expect(URL.createObjectURL).not.toHaveBeenCalled();
  });
});
