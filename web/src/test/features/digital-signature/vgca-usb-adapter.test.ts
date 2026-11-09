// TC-DSIG-VGCA-001: sign() calls vgca_sign_msg with correct args and returns SignResult shape
// TC-DSIG-VGCA-002: sign() rejects when VGCA returns error status
// TC-DSIG-VGCA-003: probeAgentStatus() classifies vgca_get_version probe outcome (3-state)
// TC-DSIG-VGCA-004: probeAgentStatus() does not throw even when plugin fails to load
//
// W1 rewrite: VGCA real contract — Status:0 (not 200), Signature field (not SignData),
// CertBase64 returned alongside signature, input is {HashAlg, HashValue, Encode} (not raw payload).

import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─── Stub document APIs before adapter module loads ────────────────────────────

// vgca-usb-adapter calls document.querySelector and document.createElement for script loading.
// Stub those to avoid real DOM script injection in jsdom.
const mockAppendChild = vi.fn();
const mockQuerySelector = vi.fn(() => null); // pretend script not yet loaded

vi.stubGlobal('document', {
  ...globalThis.document,
  querySelector: mockQuerySelector,
  createElement: (tag: string) => {
    if (tag === 'script') {
      const el: Record<string, unknown> = { src: '' };
      // Simulate onload being called asynchronously so ensurePluginLoaded resolves
      Object.defineProperty(el, 'onload', {
        set(fn: () => void) {
          Promise.resolve().then(fn);
        },
        get() { return undefined; },
      });
      return el;
    }
    return globalThis.document.createElement(tag);
  },
  head: { appendChild: mockAppendChild },
});

// ─── Pre-populate window.vgca_sign_msg so ensurePluginLoaded resolves via global check ──

const mockVgcaSignMsg = vi.fn();
vi.stubGlobal('vgca_sign_msg', mockVgcaSignMsg);

// ─── Import adapter (self-registers on import) ─────────────────────────────────

import { resolveAdapter } from '@/features/digital-signature/registry';
import '@/features/digital-signature/providers/vgca-usb-adapter';

// ─── Response helpers using real VGCA SDK contract ────────────────────────────

/** Status:0 + Signature + CertBase64 — real VGCA success shape (proven from test-bc.htm). */
function makeSuccessResponse(signature: string, certBase64 = 'Q0VSVEJBU0U2NA=='): string {
  return JSON.stringify({ Status: 0, Signature: signature, CertBase64: certBase64 });
}

/** Status:500 — VGCA error shape. */
function makeErrorResponse(msg: string, status = 500): string {
  return JSON.stringify({ Status: status, Message: msg });
}

// TC-DSIG-VGCA-001
describe('vgca-usb-adapter — TC-DSIG-VGCA-001: sign() returns SignResult on success', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // vgca_sign_msg(sender, dataToSign, callback) — real 3-arg SDK signature
    mockVgcaSignMsg.mockImplementation(
      (_sender: unknown, _data: string, cb: (s: unknown, r: string) => void) => {
        cb(_sender, makeSuccessResponse('AAAA/base64sig==', 'CERTBASE64=='));
      },
    );
  });

  it('resolves with correct signatureBase64 and providerKey', async () => {
    const adapter = resolveAdapter('usb-vgca');
    const payload = new TextEncoder().encode('hello');
    const result = await adapter.sign(payload, { reason: 'test' });

    expect(result.signatureBase64).toBe('AAAA/base64sig==');
    expect(result.providerKey).toBe('usb-vgca');
    expect(result.isPending).toBe(false);
    expect(typeof result.transactionId).toBe('string');
  });

  it('populates signerCertBase64 from CertBase64 field (W1)', async () => {
    const adapter = resolveAdapter('usb-vgca');
    const result = await adapter.sign(new TextEncoder().encode('payload'), {});
    expect(result.signerCertBase64).toBe('CERTBASE64==');
  });

  it('populates hashAlg and encode fields (W1)', async () => {
    const adapter = resolveAdapter('usb-vgca');
    const result = await adapter.sign(new Uint8Array([0x41]), { encode: 'RSA' });
    expect(result.hashAlg).toBe('SHA256');
    expect(result.encode).toBe('RSA');
  });

  it('passes {HashAlg, HashValue, Encode} JSON to vgca_sign_msg (not raw payload)', async () => {
    const adapter = resolveAdapter('usb-vgca');
    await adapter.sign(new TextEncoder().encode('test'), {});

    expect(mockVgcaSignMsg).toHaveBeenCalledTimes(1);
    const [, dataArg] = mockVgcaSignMsg.mock.calls[0] as [unknown, string, unknown];
    const parsed = JSON.parse(dataArg) as Record<string, unknown>;
    expect(parsed['HashAlg']).toBe('SHA256');
    expect(typeof parsed['HashValue']).toBe('string');
    expect((parsed['HashValue'] as string).length).toBe(44); // SHA-256 → 44 base64 chars
    expect(parsed['Encode']).toBe('RSA'); // default encode
  });
});

// TC-DSIG-VGCA-002
describe('vgca-usb-adapter — TC-DSIG-VGCA-002: sign() rejects on VGCA error status', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockVgcaSignMsg.mockImplementation(
      (_sender: unknown, _data: string, cb: (s: unknown, r: string) => void) => {
        cb(_sender, makeErrorResponse('USB token not found'));
      },
    );
  });

  it('rejects with error message from VGCA response (Status!=0)', async () => {
    const adapter = resolveAdapter('usb-vgca');
    await expect(adapter.sign(new Uint8Array([1]), {})).rejects.toThrow(
      /VGCA sign failed/,
    );
  });
});

// TC-DSIG-VGCA-003
describe('vgca-usb-adapter — TC-DSIG-VGCA-003: probeAgentStatus() uses vgca_get_version probe', () => {
  it("returns 'available' when vgca_get_version callback fires with Status=0", async () => {
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      cb(JSON.stringify({ Status: 0, Version: '1.2.3' }));
    });

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();
    expect(status).toBe('available');
  });

  it("returns 'unreachable' when vgca_get_version callback fires with non-zero Status", async () => {
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      cb(JSON.stringify({ Status: 503, Message: 'Service not running' }));
    });

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();
    expect(status).toBe('unreachable');
  });

  it("returns 'not-installed' when vgca_get_version is not a function (plugin not loaded)", async () => {
    vi.stubGlobal('vgca_get_version', undefined);
    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();
    expect(status).toBe('not-installed');
  });
});

// TC-DSIG-VGCA-004
describe('vgca-usb-adapter — TC-DSIG-VGCA-004: probeAgentStatus() does not throw on plugin load failure', () => {
  it("returns 'not-installed' without throwing when script load errors", async () => {
    // Temporarily remove globals so ensurePluginLoaded tries to inject script
    const savedSign = (globalThis as Record<string, unknown>)['vgca_sign_msg'];
    const savedVersion = (globalThis as Record<string, unknown>)['vgca_get_version'];
    delete (globalThis as Record<string, unknown>)['vgca_sign_msg'];
    delete (globalThis as Record<string, unknown>)['vgca_get_version'];

    // Override createElement to simulate script error
    const origCreateElement = globalThis.document.createElement.bind(document);
    vi.spyOn(globalThis.document, 'createElement').mockImplementationOnce((tag: string) => {
      if (tag === 'script') {
        const el: Record<string, unknown> = { src: '' };
        Object.defineProperty(el, 'onerror', {
          set(fn: () => void) {
            Promise.resolve().then(fn);
          },
          get() { return undefined; },
        });
        return el as unknown as HTMLElement;
      }
      return origCreateElement(tag);
    });

    const adapter = resolveAdapter('usb-vgca');
    const result = await adapter.probeAgentStatus!();
    expect(result).toBe('not-installed');

    // Restore
    (globalThis as Record<string, unknown>)['vgca_sign_msg'] = savedSign;
    (globalThis as Record<string, unknown>)['vgca_get_version'] = savedVersion;
    vi.restoreAllMocks();
  });
});

// TC-DSIG-VGCA-005 (W7)
describe('vgca-usb-adapter — TC-DSIG-VGCA-005: sign() rejects on user reject (Status:4002)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockVgcaSignMsg.mockImplementation(
      (_sender: unknown, _data: string, cb: (s: unknown, r: string) => void) => {
        cb(_sender, JSON.stringify({ Status: 4002, Message: 'User cancelled' }));
      },
    );
  });

  it('rejects with message containing error code or cancel indicator', async () => {
    const adapter = resolveAdapter('usb-vgca');
    await expect(adapter.sign(new Uint8Array([1]), {})).rejects.toThrow(
      /4002|cancel/i,
    );
  });
});

// TC-DSIG-VGCA-006 (W7)
describe('vgca-usb-adapter — TC-DSIG-VGCA-006: sign() builds correct JSON params with Vietnamese chars', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockVgcaSignMsg.mockImplementation(
      (_sender: unknown, _data: string, cb: (s: unknown, r: string) => void) => {
        cb(_sender, makeSuccessResponse('VN_SIG_BASE64', 'VN_CERT_BASE64'));
      },
    );
  });

  it('encodes Vietnamese UTF-8 content correctly in HashValue', async () => {
    const adapter = resolveAdapter('usb-vgca');
    // Vietnamese text: "Tài liệu" (Document)
    const vietnameseText = 'Tài liệu';
    const payload = new TextEncoder().encode(vietnameseText);

    await adapter.sign(payload, {});

    expect(mockVgcaSignMsg).toHaveBeenCalledTimes(1);
    const [, dataArg] = mockVgcaSignMsg.mock.calls[0] as [unknown, string, unknown];
    const parsed = JSON.parse(dataArg) as Record<string, unknown>;

    // Verify HashValue is base64-encoded (not corrupted ASCII)
    const hashValue = parsed['HashValue'] as string;
    expect(hashValue).toMatch(/^[A-Za-z0-9+/]+=*$/);
    // Base64 of SHA-256 is always 44 chars
    expect(hashValue.length).toBe(44);
  });
});
