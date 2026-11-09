/**
 * create-signing-request-post-body-boundary.test.ts
 *
 * Pins that createSigningRequest forwards its entire body — including sourceFileId —
 * to apiClient.post at the real serialization boundary.
 *
 * WHY this test exists: a field can be present in the constructed body object but
 * silently dropped if the call-site omits it when building the argument to apiClient.post.
 * Mocking createSigningRequest one level up (as done in use-sign-uploaded-file tests)
 * would never catch this class of regression. Spying on apiClient.post HERE pins the
 * contract at the actual wire boundary.
 *
 * Documented bug class: FE built the right object but a field was dropped before the wire
 * → prod 422 (e.g. providerKey missing from POST body, discovered in code review).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';

// Spy on apiClient BEFORE importing the module under test so the module picks up the spy.
const mockPost = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    post: (...args: unknown[]) => mockPost(...args),
  },
}));

// Import after mock is registered.
import { createSigningRequest } from '@/features/digital-signature/signature-api';
import type { CreateSigningRequestBody } from '@/features/digital-signature/signature-types';

const FILE_ID = 'file-public-id-abc123';
const DOC_ID  = 'doc-guid-xyz';

/** Minimal valid UploadedPdf body matching the shape useSignUploadedFile produces. */
const UPLOADED_PDF_BODY: CreateSigningRequestBody = {
  artifactKind: 'UploadedPdf',
  artifactRef:  DOC_ID,
  providerKey:  'usb-vgca',
  sourceFileId: FILE_ID,
  docFlow: {
    signType:           'Issued',
    originalStorageKey: `tenant-1/signing-original/${DOC_ID}.pdf`,
    docNumber:          'CV-2026-001',
    issuedDate:         '2026-06-18',
    fileName:           'contract.pdf',
  },
};

describe('createSigningRequest — apiClient.post body boundary', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Resolve with a plausible result shape so the function does not throw.
    mockPost.mockResolvedValue({
      data: {
        requestId: 'req-abc',
        clientHandoff: {
          handlerUrl: 'http://localhost:5001/api/v1/signing/docflow/handler',
          jwt:        'test-jwt',
          docId:      DOC_ID,
        },
      },
    });
  });

  it('passes sourceFileId === fileId in the POST body for the UploadedPdf path', async () => {
    await createSigningRequest(UPLOADED_PDF_BODY);

    expect(mockPost).toHaveBeenCalledOnce();

    const [path, postedBody] = mockPost.mock.calls[0] as [string, CreateSigningRequestBody];

    // Pin the route so drift is caught here too.
    expect(path).toBe('/signing/request');

    // sourceFileId must survive into the body argument passed to apiClient.post.
    // If the call-site assembled a body without this field this assertion fails at the wire.
    expect(postedBody.sourceFileId).toBe(FILE_ID);
    expect(postedBody.artifactKind).toBe('UploadedPdf');
    expect(postedBody.artifactRef).toBe(DOC_ID);
  });

  it('passes all required fields for the UploadedPdf variant into the POST body', async () => {
    await createSigningRequest(UPLOADED_PDF_BODY);

    const [, postedBody] = mockPost.mock.calls[0] as [string, CreateSigningRequestBody];

    // Every required field must reach apiClient.post — a missing field causes a BE 422.
    expect(postedBody.providerKey).toBe('usb-vgca');
    expect(postedBody.sourceFileId).toBe(FILE_ID);

    // DocFlow sub-body must be forwarded intact.
    expect(postedBody.docFlow).toBeDefined();
    expect(postedBody.docFlow!.signType).toBe('Issued');
    expect(postedBody.docFlow!.docNumber).toBe('CV-2026-001');
    expect(postedBody.docFlow!.issuedDate).toBe('2026-06-18');
    expect(postedBody.docFlow!.originalStorageKey).toContain('signing-original');
  });
});
