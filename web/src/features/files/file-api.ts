// Files API hooks — React Query wrappers for files endpoints

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import { appMessage } from '@/core/api/notification-bridge';
import type { FileRecordDto, FileUploadResultDto, FileMetadataDto, FileStatus, PagedResult } from './file-types';

const SCAN_TERMINAL: readonly FileStatus[] = ['Available', 'Rejected'];
const SCAN_POLL_INTERVAL_MS = 2000;
const SCAN_POLL_MAX_ATTEMPTS = 30; // ~60s cap at 2s interval — stop polling a stuck scan

export const fileQueryKeys = {
  list: (page: number) => ['files', 'list', page] as const,
  file: (id: string) => ['files', 'file', id] as const,
};

/** GET /api/v1/files (paginated) */
export function useFiles(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: fileQueryKeys.list(page),
    queryFn: () =>
      apiClient
        .get<PagedResult<FileRecordDto>>('/files', { params: { page, pageSize } })
        .then((r) => r.data),
  });
}

/**
 * POST /api/v1/files — multipart upload.
 * Optional onProgress reports request-upload percent (browser→server bytes, 0–100) via axios
 * onUploadProgress — distinct from the post-202 virus-scan poll (useFileScanStatus). The mutate
 * variable stays `FormData` so existing callers are unaffected; omit onProgress to opt out.
 */
export function useUploadFile(onProgress?: (pct: number) => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (formData: FormData) =>
      apiClient
        .post<FileUploadResultDto>('/files', formData, {
          headers: { 'Content-Type': 'multipart/form-data' },
          onUploadProgress: (e) => {
            // e.total is undefined when the body length is unknown — skip rather than divide by 0.
            if (onProgress && e.total) onProgress(Math.round((e.loaded / e.total) * 100));
          },
        })
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['files', 'list'] });
    },
  });
}

/**
 * Poll GET /api/v1/files/{id}/metadata after an async (202) upload until the ClamAV scan reaches a
 * terminal status (Available / Rejected) or a time cap elapses. Disabled when fileId is null.
 * Polling stops on terminal status (no point re-fetching) and at the attempt cap (a stuck scan
 * shouldn't poll forever).
 */
export function useFileScanStatus(fileId: string | null) {
  const queryClient = useQueryClient();
  const queryKey = ['files', 'scan-status', fileId] as const;
  const query = useQuery({
    queryKey,
    queryFn: () =>
      apiClient.get<FileMetadataDto>(`/files/${fileId}/metadata`).then((r) => r.data),
    enabled: Boolean(fileId),
    refetchInterval: (q) => {
      const status = q.state.data?.status;
      if (status && SCAN_TERMINAL.includes(status)) return false;
      // Count failed fetches toward the cap too — dataUpdateCount only advances on success, so a
      // persistently erroring /metadata (5xx/404) would otherwise poll forever.
      const attempts = q.state.dataUpdateCount + q.state.fetchFailureCount;
      if (attempts >= SCAN_POLL_MAX_ATTEMPTS) return false;
      return SCAN_POLL_INTERVAL_MS;
    },
    refetchIntervalInBackground: false,
  });

  // A stuck scan (clamd unreachable → status never leaves Quarantined) stops polling at the cap but
  // leaves no terminal signal. Surface that as `timedOut` so the UI can show a fallback instead of an
  // indefinitely "scanning" indicator. Read the same attempt counter the refetchInterval gate uses
  // (dataUpdateCount + fetchFailureCount) via the live query state — it advances on each poll
  // completion, the same render that flips polling off, so this resolves on the final attempt.
  const status = query.data?.status;
  const isTerminal = status ? SCAN_TERMINAL.includes(status) : false;
  const state = queryClient.getQueryState<FileMetadataDto>(queryKey);
  const attempts = (state?.dataUpdateCount ?? 0) + (state?.fetchFailureCount ?? 0);
  const timedOut = Boolean(fileId) && !isTerminal && attempts >= SCAN_POLL_MAX_ATTEMPTS;

  // Reset (not refetch) so the attempt counter returns to 0 and the poll loop resumes — a lone
  // refetch() would tick attempts past the cap and the interval gate would immediately stop again.
  const retryScan = () => {
    if (fileId) queryClient.resetQueries({ queryKey });
  };

  return { ...query, timedOut, retryScan };
}

/** DELETE /api/v1/files/{id} — soft-delete */
export function useDeleteFile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/files/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['files', 'list'] });
    },
  });
}

/**
 * Fetch a file's bytes through the authenticated apiClient. The download route is GET /files/{id}
 * (the BE FilesController streams the file there — there is no /download suffix), and it is
 * [Authorize]-gated: the Bearer token is attached by the request interceptor, which a raw <a href>
 * or <img src> can never carry. responseType:'blob' is safe past the envelope-unwrap interceptor —
 * a Blob has no `success` key so it passes through untouched.
 */
export async function fetchFileBlob(id: string): Promise<Blob> {
  const res = await apiClient.get(`/files/${id}`, { responseType: 'blob' });
  return res.data as Blob;
}

/**
 * Trigger an authenticated browser download. Fetches the bytes via apiClient (so the Bearer token
 * is sent), then saves them through a transient object URL — revoked immediately after the click to
 * avoid leaking the blob. A plain anchor pointing at the API URL would 401 (no auth header).
 */
export async function downloadFile(id: string, fileName: string): Promise<void> {
  try {
    const blob = await fetchFileBlob(id);
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(objectUrl);
  } catch (e) {
    // Log the underlying cause (401 vs 404 vs network) — the toast alone hides which failed.
    console.error('downloadFile failed', e);
    appMessage.error('Tải tệp thất bại');
  }
}

/**
 * Fetch a file's bytes as a query — for inline previews (image thumbnails) that need the blob in
 * render. Gate with `enabled` so non-image attachments don't prefetch their full bytes; they
 * download on click via downloadFile instead. The caller owns the object-URL lifecycle (create on
 * data, revoke on unmount).
 */
export function useFileBlob(fileId: string, enabled = true) {
  return useQuery({
    queryKey: ['files', 'blob', fileId],
    queryFn: () => fetchFileBlob(fileId),
    enabled: enabled && Boolean(fileId),
    staleTime: 5 * 60 * 1000,
  });
}
