/**
 * signature-api.ts — API hooks for digital-signature feature.
 * Uses apiClient (interceptor already unwraps {success,data} envelope).
 * All callers access r.data directly — NOT r.data.data.
 *
 * Route shapes verified against BE source 2026-05-11:
 *   GET  /api/v1/admin/security/signature-config/{tenantId}
 *   PUT  /api/v1/admin/security/signature-config/{tenantId}
 *   GET  /api/v1/admin/security/signature-config/available-providers
 *   POST /api/v1/digital-signature/sign
 *   POST /api/v1/digital-signature/verify
 *
 * tenantId path param: SystemAdmin passes the selected tenant ID explicitly.
 * Regular admin users are not expected to reach these endpoints (SystemAdmin-only policy).
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { NormalizedApiError } from '@/core/api/api-error';
import type {
  TenantSignatureConfigDto,
  UpdateSignatureConfigRequest,
  AvailableProviderDto,
  OcspMode,
  SignRequest,
  SignResponse,
  VerifyRequest,
  VerifyResponse,
  SigningProviderInfo,
  DocFlowMetadata,
  DocFlowSignType,
  DocFlowInitiateRequest,
  DocFlowInitiateResponse,
  CreateSigningRequestBody,
  CreateSigningRequestResult,
  SigningRequestStatusDto,
} from './signature-types';
import { SIGNING_REQUEST_TERMINAL_STATUSES } from './signature-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const signatureQueryKeys = {
  all: ['signature-config'] as const,
  config: (tenantId: string) => ['signature-config', 'detail', tenantId] as const,
  providers: () => ['signature-config', 'available-providers'] as const,
};

export const signingRequestQueryKeys = {
  status: (publicId: string) => ['signing-request', 'status', publicId] as const,
};

// ─── Admin config queries ─────────────────────────────────────────────────────

/**
 * GET /api/v1/admin/security/signature-config/{tenantId}
 * tenantId: UUID of the tenant whose config to load (SystemAdmin-only endpoint).
 *
 * 404 is the EXPECTED EMPTY STATE — tenant has no config yet, user setups via Edit.
 * Page-level info Alert handles the empty-state UX (signature-config-page.tsx:182-186).
 * Re-throwing 404 would incorrectly fire two redundant error reactions:
 *   (a) global QueryCache.onError red toast (query-config.ts:39-52)
 *   (b) page-level error Alert "Không thể tải cấu hình chữ ký số" (line 122-128)
 * Both contradict the truth-of-design ("missing config = empty initial state, not error").
 */
export function useSignatureConfig(tenantId: string) {
  return useQuery({
    queryKey: signatureQueryKeys.config(tenantId),
    queryFn: async (): Promise<TenantSignatureConfigDto | null> => {
      try {
        const r = await apiClient.get<TenantSignatureConfigDto>(
          `/admin/security/signature-config/${tenantId}`,
        );
        return r.data;
      } catch (err) {
        if ((err as NormalizedApiError | undefined)?.status === 404) return null;
        throw err;
      }
    },
    enabled: !!tenantId,
  });
}

/**
 * GET /api/v1/digital-signature/provider-info
 * Non-admin, JWT-tenant-scoped: returns the active provider key + capabilities the form-fill
 * signing modal needs. Unlike useSignatureConfig (SystemAdmin-only), any authenticated form
 * filler may call this. Always resolves (200) — a tenant without config returns isEnabled=false.
 */
export function useSigningProviderInfo(enabled = true) {
  return useQuery({
    queryKey: ['signing-provider-info'] as const,
    queryFn: () =>
      apiClient.get<SigningProviderInfo>('/digital-signature/provider-info').then((r) => r.data),
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}

/** GET /api/v1/admin/security/signature-config/available-providers */
export function useAvailableProviders() {
  return useQuery({
    queryKey: signatureQueryKeys.providers(),
    queryFn: () =>
      apiClient
        .get<AvailableProviderDto[]>('/admin/security/signature-config/available-providers')
        .then((r) => r.data),
  });
}

/**
 * PUT /api/v1/admin/security/signature-config/{tenantId}
 * Returns 204 No Content on success (upsert semantics).
 */
export function useUpdateSignatureConfig(tenantId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateSignatureConfigRequest) =>
      apiClient
        .put(`/admin/security/signature-config/${tenantId}`, body)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: signatureQueryKeys.config(tenantId) });
    },
  });
}

// ─── Signing / verify mutations ───────────────────────────────────────────────

/** POST /api/v1/digital-signature/sign */
export async function callSign(body: SignRequest): Promise<SignResponse> {
  const r = await apiClient.post<SignResponse>('/digital-signature/sign', body);
  return r.data;
}

/** POST /api/v1/digital-signature/verify */
export async function callVerify(body: VerifyRequest): Promise<VerifyResponse> {
  const r = await apiClient.post<VerifyResponse>('/digital-signature/verify', body);
  return r.data;
}

/**
 * POST /api/v1/digital-signature/verify-xml (W2 Tier-2)
 * Verifies an XMLDSig-signed XML document.
 * xmlDsigBase64: base64-encoded signed XML bytes (from vgca-usb-adapter signXml()).
 * ocspModeOverride: null = use tenant config default; 'Hard' enforces strict OCSP.
 */
export async function callVerifyXml(
  xmlDsigBase64: string,
  ocspModeOverride?: OcspMode,
): Promise<VerifyResponse> {
  const r = await apiClient.post<VerifyResponse>('/digital-signature/verify-xml', {
    xmlDsigBytes: xmlDsigBase64,
    ocspModeOverride: ocspModeOverride ?? null,
  });
  return r.data;
}

// ─── Doc-flow initiation (W3 Tier-3) ─────────────────────────────────────────

/**
 * POST /api/signing/docflow/initiate
 * Initiates a Tier-3 VGCA doc-flow signing session.
 * Returns {handlerUrl, jwt, docId} for the FE to pass into the VGCA plugin call.
 *
 * originalStorageKey must point to the unsigned PDF in the "signing-original" bucket.
 */
export async function initiateDocFlow(
  docId: string,
  signType: DocFlowSignType,
  metadata: DocFlowMetadata,
): Promise<DocFlowInitiateResponse> {
  const body: DocFlowInitiateRequest = {
    docId,
    signType,
    originalStorageKey: metadata.originalStorageKey,
    docNumber:          metadata.docNumber,
    issuedDate:         metadata.issuedDate.toISOString().slice(0, 10),
    fileName:           metadata.fileName ?? null,
    extra:              metadata.extra ?? null,
  };
  const r = await apiClient.post<DocFlowInitiateResponse>('/signing/docflow/initiate', body);
  return r.data;
}

/**
 * POST /api/v1/digital-signature/verify-jws (W2 Tier-2)
 * Verifies a JWS compact serialization.
 * jwsCompact: the JWS string (header.payload.signature) from vgca-usb-adapter signJson().
 * ocspModeOverride: null = use tenant config default; 'Hard' enforces strict OCSP.
 */
export async function callVerifyJws(
  jwsCompact: string,
  ocspModeOverride?: OcspMode,
): Promise<VerifyResponse> {
  const r = await apiClient.post<VerifyResponse>('/digital-signature/verify-jws', {
    jwsCompact,
    ocspModeOverride: ocspModeOverride ?? null,
  });
  return r.data;
}

// ─── Provider-agnostic signing request endpoints ──────────────────────────────

/**
 * POST /api/v1/signing/request — create a durable, pollable signing request.
 * Returns the public id + strategy client hand-off (for DocFlow: handlerUrl/jwt/docId).
 * The relative path below resolves to /api/v1/signing/request because apiClient.baseURL is "/api/v1".
 */
export async function createSigningRequest(
  body: CreateSigningRequestBody,
): Promise<CreateSigningRequestResult> {
  // A tenant configured for the SYNC remote flow blocks this request on the mobile OTP (~120s) before returning;
  // the ASYNC flow returns in milliseconds. Use a generous timeout so the sync path is not aborted by the default
  // 30s axios timeout — harmless for async (it still returns immediately).
  const r = await apiClient.post<CreateSigningRequestResult>('/signing/request', body, {
    timeout: 150_000, // ~120s OTP wait + margin (matches the dev PAdES spike call)
  });
  return r.data;
}

/**
 * GET /api/v1/signing/request/{publicId}/status — read-time status projection for polling.
 * Returns the current lifecycle status without storing it on the aggregate (no drift).
 */
export async function getSigningRequestStatus(
  publicId: string,
): Promise<SigningRequestStatusDto> {
  const r = await apiClient.get<SigningRequestStatusDto>(`/signing/request/${publicId}/status`);
  return r.data;
}

/**
 * GET /api/v1/signing/request/{publicId}/signed-pdf — download the BE-assembled signed PDF for a Completed
 * server-side PAdES request. Returns the raw PDF blob (responseType 'blob' so the binary body is not parsed as
 * JSON). Throws a NormalizedApiError on 400 (still pending / not a PAdES request) or 404 (cross-tenant).
 */
export async function downloadSignedPdf(publicId: string): Promise<Blob> {
  const r = await apiClient.get<Blob>(`/signing/request/${publicId}/signed-pdf`, {
    responseType: 'blob',
  });
  return r.data;
}

// Poll constants — match sibling file-scan hook convention for consistency.
const SIGNING_REQUEST_POLL_INTERVAL_MS = 2000;
const SIGNING_REQUEST_POLL_MAX_ATTEMPTS = 60; // ~2 min cap at 2s — a signing ceremony can take longer than a virus scan

/**
 * Poll GET /api/signing/request/{publicId}/status until the request reaches a terminal status
 * (Completed/Failed/Expired) or the attempt cap is hit. Disabled when publicId is undefined.
 *
 * Mirrors the useFileScanStatus pattern:
 *   - refetchInterval returns false once terminal or cap reached.
 *   - timedOut is surfaced so callers can show a fallback instead of an indefinite spinner.
 *   - retryScan (as retryPoll here) resets the query (zeros counters) so the loop can resume.
 *
 * Query key: ['signing-request', 'status', publicId]
 */
export function useSigningRequestStatus(publicId: string | undefined) {
  const queryClient = useQueryClient();
  const queryKey = publicId
    ? signingRequestQueryKeys.status(publicId)
    : (['signing-request', 'status', ''] as const);

  const query = useQuery({
    queryKey,
    queryFn: () => getSigningRequestStatus(publicId!),
    enabled: Boolean(publicId),
    refetchInterval: (q) => {
      const status = q.state.data?.status;
      if (status && SIGNING_REQUEST_TERMINAL_STATUSES.includes(status)) return false;
      // Count failed fetches toward the cap so a persistently erroring endpoint doesn't poll forever.
      const attempts = q.state.dataUpdateCount + q.state.fetchFailureCount;
      if (attempts >= SIGNING_REQUEST_POLL_MAX_ATTEMPTS) return false;
      return SIGNING_REQUEST_POLL_INTERVAL_MS;
    },
    refetchIntervalInBackground: false,
  });

  // Surface timedOut: cap reached but no terminal status yet (e.g. signing ceremony hung).
  const status = query.data?.status;
  const isTerminal = status ? SIGNING_REQUEST_TERMINAL_STATUSES.includes(status) : false;
  const state = queryClient.getQueryState<SigningRequestStatusDto>(queryKey);
  const attempts = (state?.dataUpdateCount ?? 0) + (state?.fetchFailureCount ?? 0);
  const timedOut = Boolean(publicId) && !isTerminal && attempts >= SIGNING_REQUEST_POLL_MAX_ATTEMPTS;

  // Reset (not refetch) so the attempt counter returns to 0 and the poll loop can resume.
  const retryPoll = () => {
    if (publicId) queryClient.resetQueries({ queryKey });
  };

  return { ...query, timedOut, retryPoll };
}
