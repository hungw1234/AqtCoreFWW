/**
 * vgca-docflow-methods-refactor.test.ts
 *
 * Verifies the runVgcaPlugin extraction and signTypeToPluginFn map introduced
 * to support the uploaded-PDF signing path (which already has a handoff from
 * createSigningRequest and must NOT call /initiate again to avoid duplicate records).
 *
 * Also confirms that the existing _invokeDocFlow-based methods (signIssued etc.)
 * still work correctly after the refactor — they now delegate plugin invocation to
 * runVgcaPlugin internally.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// Mock initiateDocFlow before importing the module under test
const mockInitiateDocFlow = vi.fn();
vi.mock('@/features/digital-signature/signature-api', () => ({
  initiateDocFlow: (...args: unknown[]) => mockInitiateDocFlow(...args),
}));

import {
  signTypeToPluginFn,
  runVgcaPlugin,
  SIGN_TYPE_TO_PLUGIN_FN,
  signIssued,
  signApproved,
} from '@/features/digital-signature/providers/vgca-docflow-methods';
import type { DocFlowSignTypeName } from '@/features/digital-signature/signature-types';

// runVgcaPlugin now awaits ensureVgcaPluginLoaded() before touching the plugin global.
// Pre-define vgca_sign_msg so the loader short-circuits (the SDK script cannot actually
// load in jsdom); these tests exercise the plugin-invocation contract, not the loader.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(window as any)['vgca_sign_msg'] = vi.fn();

// ─── Fixtures ──────────────────────────────────────────────────────────────────

const HANDOFF = {
  handlerUrl: 'https://example.com/handler',
  jwt: 'tok.tok.tok',
  docId: 'dddddddd-0000-0000-0000-000000000001',
};

const METADATA = {
  docNumber: 'SỐ-001/2026',
  issuedDate: new Date('2026-06-17'),
  fileName: 'test.pdf',
};

const DOC_FLOW_METADATA = {
  ...METADATA,
  originalStorageKey: 'tenant-id/test.pdf',
};

function makeSuccessResponse() {
  return JSON.stringify({ Status: 0, FileServer: 'https://storage/signed.pdf' });
}

// ─── signTypeToPluginFn ────────────────────────────────────────────────────────

describe('signTypeToPluginFn', () => {
  const cases: Array<[DocFlowSignTypeName, string]> = [
    ['Issued',   'vgca_sign_issued'],
    ['Approved', 'vgca_sign_approved'],
    ['Income',   'vgca_sign_income'],
    ['Comment',  'vgca_comment'],
    ['Appendix', 'vgca_sign_appendix'],
    ['Copy',     'vgca_sign_copy'],
    ['Files',    'vgca_sign_files'],
  ];

  it.each(cases)('%s → %s', (signType, pluginFn) => {
    expect(signTypeToPluginFn(signType)).toBe(pluginFn);
  });

  it('SIGN_TYPE_TO_PLUGIN_FN covers all 7 sign types', () => {
    const keys = Object.keys(SIGN_TYPE_TO_PLUGIN_FN);
    expect(keys).toHaveLength(7);
  });
});

// ─── runVgcaPlugin ─────────────────────────────────────────────────────────────

describe('runVgcaPlugin', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any)['vgca_sign_issued'];
  });

  it('resolves with Completed status when plugin fires Status=0', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_issued'] = vi.fn(
      (_params: string, cb: (r: string) => void) => {
        Promise.resolve().then(() => cb(makeSuccessResponse()));
      },
    );

    const result = await runVgcaPlugin('vgca_sign_issued', HANDOFF, METADATA);

    expect(result.status).toBe('Completed');
    expect(result.storageKey).toBe('https://storage/signed.pdf');
  });

  it('treats a string "0" Status as success (agent may send Status as a string)', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_issued'] = vi.fn(
      (_params: string, cb: (r: string) => void) => {
        Promise.resolve().then(() =>
          cb(JSON.stringify({ Status: '0', FileServer: 'https://storage/signed.pdf' })),
        );
      },
    );

    const result = await runVgcaPlugin('vgca_sign_issued', HANDOFF, METADATA);

    expect(result.status).toBe('Completed');
    expect(result.storageKey).toBe('https://storage/signed.pdf');
  });

  it('rejects when plugin fires non-zero Status', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_issued'] = vi.fn(
      (_params: string, cb: (r: string) => void) => {
        Promise.resolve().then(() =>
          cb(JSON.stringify({ Status: 1, Message: 'USB token not ready' })),
        );
      },
    );

    await expect(runVgcaPlugin('vgca_sign_issued', HANDOFF, METADATA)).rejects.toThrow(
      'vgca_sign_issued failed (Status=1): USB token not ready',
    );
  });

  it('rejects with "not available" when plugin global is absent', async () => {
    // Ensure the global is absent
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any)['vgca_sign_issued'];

    await expect(runVgcaPlugin('vgca_sign_issued', HANDOFF, METADATA)).rejects.toThrow(
      'vgca_sign_issued not available',
    );
  });

  it('passes the params JSON as the FIRST arg (2-arg DocFlow contract, no sender)', async () => {
    let capturedParams: Record<string, unknown> | null = null;
    // The DocFlow plugin contract is (paramsJson, cb) — NO sender. Capturing arg0 as the
    // params string pins this: if the code regressed to passing a sender first, arg0 would be
    // the sender (a uuid, not JSON) and JSON.parse below would throw — failing this test.
    const pluginFn = vi.fn((params: string, cb: (r: string) => void) => {
      capturedParams = JSON.parse(params) as Record<string, unknown>;
      Promise.resolve().then(() => cb(makeSuccessResponse()));
    });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_issued'] = pluginFn;

    await runVgcaPlugin('vgca_sign_issued', HANDOFF, METADATA);

    expect(pluginFn.mock.calls[0]).toHaveLength(2); // (paramsJson, cb) — no sender
    expect(capturedParams).not.toBeNull();
    expect(capturedParams!['FileUploadHandler']).toBe(HANDOFF.handlerUrl);
    expect(capturedParams!['SessionId']).toBe(''); // JWT auth path (sample parity)
    expect(capturedParams!['JWTToken']).toBe(HANDOFF.jwt);
    // FileName is the absolute download URL the desktop tool fetches the document from — NOT a
    // bare display name. A relative name throws UriFormatException inside the tool (0x0023). It must
    // carry the docId + token the gated [AllowAnonymous] download endpoint requires.
    const fileName = capturedParams!['FileName'] as string;
    expect(fileName).toBe(
      `${HANDOFF.handlerUrl}?docId=${encodeURIComponent(HANDOFF.docId)}&token=${encodeURIComponent(HANDOFF.jwt)}`,
    );
    expect(/^https?:\/\//.test(fileName)).toBe(true); // absolute URL — never a bare filename
    expect(capturedParams!['DocNumber']).toBe(METADATA.docNumber);
    expect(capturedParams!['IssuedDate']).toBe('2026-06-17');
  });

  it('does NOT call initiateDocFlow (no /initiate side-effect)', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_issued'] = vi.fn(
      (_params: string, cb: (r: string) => void) => {
        Promise.resolve().then(() => cb(makeSuccessResponse()));
      },
    );

    await runVgcaPlugin('vgca_sign_issued', HANDOFF, METADATA);

    // runVgcaPlugin must not trigger a second /initiate — that would create a duplicate record.
    expect(mockInitiateDocFlow).not.toHaveBeenCalled();
  });
});

// ─── Existing public methods still work after refactor ────────────────────────

describe('signIssued (existing method — refactor regression)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any)['vgca_sign_issued'];
  });

  it('calls initiateDocFlow then invokes the VGCA plugin', async () => {
    mockInitiateDocFlow.mockResolvedValueOnce(HANDOFF);

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_issued'] = vi.fn(
      (_params: string, cb: (r: string) => void) => {
        Promise.resolve().then(() => cb(makeSuccessResponse()));
      },
    );

    const result = await signIssued(HANDOFF.docId, DOC_FLOW_METADATA);

    expect(mockInitiateDocFlow).toHaveBeenCalledOnce();
    expect(result.status).toBe('Completed');
  });
});

describe('signApproved (existing method — refactor regression)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any)['vgca_sign_approved'];
  });

  it('calls initiateDocFlow and routes to vgca_sign_approved', async () => {
    mockInitiateDocFlow.mockResolvedValueOnce(HANDOFF);

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any)['vgca_sign_approved'] = vi.fn(
      (_params: string, cb: (r: string) => void) => {
        Promise.resolve().then(() => cb(makeSuccessResponse()));
      },
    );

    const result = await signApproved(HANDOFF.docId, DOC_FLOW_METADATA);

    expect(mockInitiateDocFlow).toHaveBeenCalledOnce();
    expect(result.status).toBe('Completed');
  });
});
