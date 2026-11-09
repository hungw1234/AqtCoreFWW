/**
 * signatures-api.ts — hand-written hooks for the self-service signature management tab (Phase 6 Slice A).
 * Uses apiClient (interceptor unwraps {success,data} envelope) → callers read r.data directly.
 *
 * Routes verified against BE source 2026-06-30:
 *   GET    /api/v1/me/signature-overview
 *   POST   /api/v1/me/signature-profiles                  { displayName }
 *   PUT    /api/v1/me/signature-profiles/{publicId}        { displayName }
 *   DELETE /api/v1/me/signature-profiles/{publicId}
 *   POST   /api/v1/me/signature-profiles/{publicId}/default  (no body)
 *   POST   /api/v1/me/signature-profiles/{publicId}/images    (multipart: visualType + file)
 *
 * All profile mutations invalidate the overview query — it is the single source the tab renders.
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  SignatureOverviewDto,
  CreateSignatureProfileRequest,
  UpdateSignatureProfileRequest,
  SetSigningNationalIdRequest,
  EnrollmentChallengeDto,
  EnrollSignatureCertRequest,
  SignatureVisualType,
} from './signature-types';

// Remote (Mysign) enroll blocks server-side on the signer's phone OTP; the same generous timeout the PAdES
// create-signing-request uses (default axios 30s would abort the OTP wait). Harmless for the fast USB path.
const ENROLL_TIMEOUT_MS = 150_000;

// ─── Query keys ──────────────────────────────────────────────────────────────

// 'me' root mirrors the tenant-scoped key convention (query-config.ts) so a broad me-scope
// invalidation reaches this cache.
export const signatureProfileQueryKeys = {
  overview: ['me', 'signature-overview'] as const,
};

// ─── Overview (the tab's single read) ───────────────────────────────────────────

/**
 * GET /api/v1/me/signature-overview — profiles + enrollment approval-status for the current user.
 * Always resolves (200) for an authenticated user; an empty profile/enrollment list is the normal
 * first-time state, not an error.
 */
export function useSignatureOverview(enabled = true) {
  return useQuery({
    queryKey: signatureProfileQueryKeys.overview,
    queryFn: async (): Promise<SignatureOverviewDto> => {
      const r = await apiClient.get<SignatureOverviewDto>('/me/signature-overview');
      return r.data;
    },
    enabled,
  });
}

// ─── Profile mutations ───────────────────────────────────────────────────────────

/** POST /api/v1/me/signature-profiles — create a profile (DisplayName only; image deferred to M2). */
export function useCreateSignatureProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSignatureProfileRequest) =>
      apiClient.post('/me/signature-profiles', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}

/** PUT /api/v1/me/signature-profiles/{publicId} — rename a profile. */
export function useUpdateSignatureProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: UpdateSignatureProfileRequest }) =>
      apiClient.put(`/me/signature-profiles/${publicId}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}

/** DELETE /api/v1/me/signature-profiles/{publicId} — remove a profile. */
export function useDeleteSignatureProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (publicId: string) =>
      apiClient.delete(`/me/signature-profiles/${publicId}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}

/** POST /api/v1/me/signature-profiles/{publicId}/default — mark a profile as the default (no body). */
export function useSetDefaultSignatureProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (publicId: string) =>
      apiClient.post(`/me/signature-profiles/${publicId}/default`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}

/**
 * POST /api/v1/me/signature-profiles/{publicId}/images — upload one visual signature image slot.
 * Multipart body: `visualType` (the enum NAME FullSignature|InitialSignature) + `file`. The image may come
 * from a file picker OR a drawn canvas (exported to a PNG File) — same wire shape either way. BE re-validates
 * (magic-byte PNG/JPEG narrowing, SVG-reject, ≤500KB), so the client checks are UX-only, not the security gate.
 * Invalidates the overview so the tab's hasFullImage / hasInitialImage badges refresh.
 */
export function useUploadSignatureImage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      publicId,
      visualType,
      file,
    }: {
      publicId: string;
      visualType: SignatureVisualType;
      file: File;
    }) => {
      const form = new FormData();
      form.append('visualType', visualType);
      form.append('file', file);
      return apiClient
        .post(`/me/signature-profiles/${publicId}/images`, form, {
          headers: { 'Content-Type': 'multipart/form-data' },
        })
        .then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}

/**
 * GET /api/v1/me/signature-profiles/{publicId}/images/{visualType} — the raw bytes of one stored image slot.
 * Returns a Blob (image/png|jpeg) the caller turns into an objectURL for an <img> preview. responseType:'blob'
 * bypasses the envelope-unwrap interceptor (a Blob has no `success` key). Throws (via apiClient) on 404 (slot
 * empty / profile not the caller's) or 401 — the caller only fetches when the overview says the slot is filled.
 */
export async function fetchSignatureImageBlob(
  publicId: string,
  visualType: SignatureVisualType,
): Promise<Blob> {
  const r = await apiClient.get(`/me/signature-profiles/${publicId}/images/${visualType}`, {
    responseType: 'blob',
  });
  return r.data as Blob;
}

// ─── Enrollment (Slice B) ────────────────────────────────────────────────────────

/**
 * PUT /api/v1/me/signing-national-id — self-service set/change of the caller's CCCD (prerequisite for enroll).
 * 400 SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT when an active enrollment exists; 422 when not 12 digits.
 * Invalidates the overview so `nationalIdEstablished` flips without a manual refetch.
 */
export function useSetSigningNationalId() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SetSigningNationalIdRequest) =>
      apiClient.put('/me/signing-national-id', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}

/** POST /api/v1/me/signature-cert/challenge — mint a one-time enrollment nonce + challenge. */
export function useIssueEnrollmentChallenge() {
  return useMutation({
    mutationFn: () =>
      apiClient.post<EnrollmentChallengeDto>('/me/signature-cert/challenge').then((r) => r.data),
  });
}

/**
 * POST /api/v1/me/signature-cert/enroll — submit the enrollment (kind-agnostic). Remote sends only
 * {kind, nonceId} and blocks on the phone OTP; USB adds the locally signed nonce + DER cert. Long timeout
 * for the Remote OTP wait. Invalidates the overview so the new Pending enrollment row appears.
 */
export function useEnrollSignatureCert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: EnrollSignatureCertRequest) =>
      apiClient.post('/me/signature-cert/enroll', body, { timeout: ENROLL_TIMEOUT_MS }).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: signatureProfileQueryKeys.overview }),
  });
}
