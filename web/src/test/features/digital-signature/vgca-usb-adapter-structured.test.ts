// TC-DSIG-VGCA-XML-001: signXml() calls vgca_sign_xml with correct param JSON shape
// TC-DSIG-VGCA-XML-002: signXml() rejects when VGCA returns non-zero Status
// TC-DSIG-VGCA-JSON-001: signJson() calls vgca_sign_json with correct param JSON shape
// TC-DSIG-VGCA-JSON-002: signJson() rejects when VGCA returns non-zero Status with Message field
//
// W2 Tier-2: structured signing methods (vgca_sign_xml / vgca_sign_json).
// Tests verify: (a) param JSON shape sent to SDK, (b) Status:0 resolves base64,
// (c) Status!=0 rejects with error message.

import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─── Stub document APIs before adapter module loads ────────────────────────────

const mockAppendChild = vi.fn();
const mockQuerySelector = vi.fn(() => null);

vi.stubGlobal('document', {
  ...globalThis.document,
  querySelector: mockQuerySelector,
  createElement: (tag: string) => {
    if (tag === 'script') {
      const el: Record<string, unknown> = { src: '' };
      Object.defineProperty(el, 'onload', {
        set(fn: () => void) { Promise.resolve().then(fn); },
        get() { return undefined; },
      });
      return el;
    }
    return globalThis.document.createElement(tag);
  },
  head: { appendChild: mockAppendChild },
});

// ─── Pre-populate window globals so ensurePluginLoaded short-circuits ─────────

const mockVgcaSignMsg  = vi.fn();
const mockVgcaSignXml  = vi.fn();
const mockVgcaSignJson = vi.fn();

vi.stubGlobal('vgca_sign_msg',  mockVgcaSignMsg);
vi.stubGlobal('vgca_sign_xml',  mockVgcaSignXml);
vi.stubGlobal('vgca_sign_json', mockVgcaSignJson);

// ─── Import adapter (self-registers on import) ─────────────────────────────────

import { resolveAdapter } from '@/features/digital-signature/registry';
import '@/features/digital-signature/providers/vgca-usb-adapter';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Simulate VGCA success callback — Status:0 + Signature field. */
function respondSuccess(sigBase64: string) {
  return (_sender: unknown, params: string, cb: (s: unknown, r: string) => void) => {
    void params; // capture params for assertion via mockVgcaSignXml.mock.calls
    cb(null, JSON.stringify({ Status: 0, Signature: sigBase64 }));
  };
}

/** Simulate VGCA failure callback — non-zero Status + error message. */
function respondFailure(status: number, msg: string, errorField: 'Error' | 'Message' = 'Error') {
  return (_sender: unknown, _params: string, cb: (s: unknown, r: string) => void) => {
    cb(null, JSON.stringify({ Status: status, [errorField]: msg }));
  };
}

const SAMPLE_XML_SIG_B64  = 'c2lnbmVkWE1M'; // base64("signedXML") placeholder
const SAMPLE_JWS_B64      = 'aGVhZGVyLnBheWxvYWQuc2ln'; // placeholder

// ─── Suite ───────────────────────────────────────────────────────────────────

describe('vgca-usb-adapter — W2 Tier-2 structured signing', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── TC-DSIG-VGCA-XML-001 ─────────────────────────────────────────────────────

  it('signXml: calls vgca_sign_xml with correct param JSON and resolves base64 on Status:0', async () => {
    mockVgcaSignXml.mockImplementation(respondSuccess(SAMPLE_XML_SIG_B64));

    const adapter = resolveAdapter('usb-vgca');
    expect(adapter.signXml).toBeDefined();

    const xmlContent = '<Root><Data>test</Data></Root>';
    const result = await adapter.signXml!(xmlContent, { hashAlg: 'SHA256', form: 'Enveloped' });

    expect(result).toBe(SAMPLE_XML_SIG_B64);
    expect(mockVgcaSignXml).toHaveBeenCalledOnce();

    // Verify the params JSON shape passed to SDK
    const [_sender, paramsJson] = mockVgcaSignXml.mock.calls[0] as [unknown, string, unknown];
    const params = JSON.parse(paramsJson) as Record<string, unknown>;

    expect(params).toMatchObject({
      HashAlg: 'SHA256',
      XmlDsigForm: 'Enveloped',
      ReturnSignatureOnly: 'false',
    });
    // Base64Content must be present and non-empty (XML encoded as base64 via TextEncoder)
    expect(typeof params['Base64Content']).toBe('string');
    expect((params['Base64Content'] as string).length).toBeGreaterThan(0);
  });

  // ── TC-DSIG-VGCA-XML-002 ─────────────────────────────────────────────────────

  it('signXml: rejects with error message when VGCA returns non-zero Status', async () => {
    mockVgcaSignXml.mockImplementation(
      respondFailure(1, 'XML signing failed: token not ready', 'Error'),
    );

    const adapter = resolveAdapter('usb-vgca');
    await expect(
      adapter.signXml!('<Root/>', {}),
    ).rejects.toThrow(/vgca_sign_xml failed \(Status=1\)/);
  });

  // ── TC-DSIG-VGCA-JSON-001 ─────────────────────────────────────────────────────

  it('signJson: calls vgca_sign_json with correct param JSON and resolves base64 on Status:0', async () => {
    mockVgcaSignJson.mockImplementation(respondSuccess(SAMPLE_JWS_B64));

    const adapter = resolveAdapter('usb-vgca');
    expect(adapter.signJson).toBeDefined();

    const jsonContent = JSON.stringify({ id: 'doc-1', amount: 1000 });
    const result = await adapter.signJson!(jsonContent, { detach: false, usePss: true });

    expect(result).toBe(SAMPLE_JWS_B64);
    expect(mockVgcaSignJson).toHaveBeenCalledOnce();

    // Verify the params JSON shape passed to SDK
    const [_sender, paramsJson] = mockVgcaSignJson.mock.calls[0] as [unknown, string, unknown];
    const params = JSON.parse(paramsJson) as Record<string, unknown>;

    expect(params).toMatchObject({
      JsonContent: jsonContent,
      DetachPayload: 'false',
      IsPss: 'true',
    });
  });

  // ── TC-DSIG-VGCA-JSON-002 ─────────────────────────────────────────────────────

  it('signJson: rejects with Message field text when VGCA returns non-zero Status', async () => {
    // JSON failure uses Message field (not Error) per DefaultECC.htm SDK sample
    mockVgcaSignJson.mockImplementation(
      respondFailure(2, 'PIN locked — too many attempts', 'Message'),
    );

    const adapter = resolveAdapter('usb-vgca');
    await expect(
      adapter.signJson!(JSON.stringify({ id: 'x' }), {}),
    ).rejects.toThrow(/vgca_sign_json failed \(Status=2\)/);
  });
});
