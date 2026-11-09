/**
 * signed-document-api-wire-contract.test.ts
 *
 * Pins the EXACT URL, query-string, and bearer-header contract for:
 *   fetchSigningSteps  — GET /api/v1/files/{id}/signing-steps  and
 *                        GET /api/v1/forms/submissions/{id}/signing-steps
 *   fetchSignedStepBlob — GET /api/v1/files/{id}/signed?requestId={guid}  and
 *                         GET /api/v1/forms/submissions/{id}/signed?sequence=N
 *
 * All requests go through apiClient whose request interceptor adds the Bearer
 * header — the tests assert apiClient.get is called with the right URL/options
 * (not raw axios). If the URL changes the feature silently breaks against the BE,
 * so every path segment and query param is asserted explicitly.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─── Mock apiClient transport ──────────────────────────────────────────────────

const mockGet = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => mockGet(...args),
  },
}));

import { fetchSigningSteps, fetchSignedStepBlob } from '@/features/digital-signature/signed-document/signed-document-api';
import type { SigningStepRow } from '@/features/digital-signature/signed-document/signing-step-types';

// ─── Fixtures ─────────────────────────────────────────────────────────────────

const FILE_ID = 'aaaaaaaa-1111-1111-1111-000000000001';
const FORM_ID = 'bbbbbbbb-2222-2222-2222-000000000002';

const STEP_ROW: SigningStepRow = {
  signingRequestId: 'cccccccc-3333-3333-3333-000000000003',
  sequenceNumber: 1,
  status: 'Completed',
  signerUserId: 'user-abc',
  signerName: null,
  completedAt: '2026-06-18T10:00:00Z',
  hasSignedPdf: true,
};

const PDF_BLOB = new Blob(['%PDF-1.4'], { type: 'application/pdf' });

// ─── fetchSigningSteps ────────────────────────────────────────────────────────

describe('fetchSigningSteps — files scope', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the exact files steps URL and returns unwrapped rows', async () => {
    mockGet.mockResolvedValue({ data: [STEP_ROW] });

    const result = await fetchSigningSteps('files', FILE_ID);

    // Verify the exact URL passed to apiClient.get (no base URL — interceptor prepends /api/v1)
    expect(mockGet).toHaveBeenCalledOnce();
    const [url] = mockGet.mock.calls[0] as [string];
    expect(url).toBe(`/files/${FILE_ID}/signing-steps`);

    // apiClient interceptor already unwraps the { data: T } envelope; we return r.data directly
    expect(result).toEqual([STEP_ROW]);
  });

  it('returns empty array when BE returns empty (no signing exists)', async () => {
    mockGet.mockResolvedValue({ data: [] });

    const result = await fetchSigningSteps('files', FILE_ID);

    expect(result).toEqual([]);
  });
});

describe('fetchSigningSteps — forms scope', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the exact forms submissions steps URL', async () => {
    mockGet.mockResolvedValue({ data: [STEP_ROW] });

    await fetchSigningSteps('forms', FORM_ID);

    const [url] = mockGet.mock.calls[0] as [string];
    expect(url).toBe(`/forms/submissions/${FORM_ID}/signing-steps`);
  });
});

// ─── fetchSignedStepBlob ──────────────────────────────────────────────────────

describe('fetchSignedStepBlob — files scope', () => {
  beforeEach(() => vi.clearAllMocks());

  it('addresses the files signed URL by the per-attempt requestId (not sequence)', async () => {
    mockGet.mockResolvedValue({ data: PDF_BLOB });

    const blob = await fetchSignedStepBlob('files', FILE_ID, STEP_ROW);

    expect(mockGet).toHaveBeenCalledOnce();
    const [url, options] = mockGet.mock.calls[0] as [string, { responseType: string }];
    // Files address each independent sign by its SigningRequest id so re-signs (all sequence 1)
    // each resolve to their own blob — addressing by sequence would always return the latest.
    expect(url).toBe(`/files/${FILE_ID}/signed?requestId=${STEP_ROW.signingRequestId}`);
    // responseType:'blob' bypasses the envelope-unwrap interceptor (Blob has no `success` key)
    expect(options?.responseType).toBe('blob');
    expect(blob).toBe(PDF_BLOB);
  });

  it('uses the row\'s signingRequestId in the URL', async () => {
    mockGet.mockResolvedValue({ data: PDF_BLOB });

    await fetchSignedStepBlob('files', FILE_ID, { ...STEP_ROW, signingRequestId: 'dddddddd-4444-4444-4444-000000000004' });

    const [url] = mockGet.mock.calls[0] as [string];
    expect(url).toBe(`/files/${FILE_ID}/signed?requestId=dddddddd-4444-4444-4444-000000000004`);
  });
});

describe('fetchSignedStepBlob — forms scope', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the exact forms submissions signed URL by sequence', async () => {
    mockGet.mockResolvedValue({ data: PDF_BLOB });

    await fetchSignedStepBlob('forms', FORM_ID, { ...STEP_ROW, sequenceNumber: 2 });

    const [url, options] = mockGet.mock.calls[0] as [string, { responseType: string }];
    expect(url).toBe(`/forms/submissions/${FORM_ID}/signed?sequence=2`);
    expect(options?.responseType).toBe('blob');
  });
});
