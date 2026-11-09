// TC-DSIG-REMOTE-001: sign() calls callSign with correct payload (base64) and reason
// TC-DSIG-REMOTE-002: sign() maps callSign response to SignResult shape correctly
// TC-DSIG-REMOTE-003: sign() propagates rejection from callSign
// TC-DSIG-REMOTE-004: sign() throws when isSuccess=false and isPending=false
// TC-DSIG-REMOTE-005: 'remote' fallback adapter also registered
// TC-DSIG-REMOTE-006: 'remote-viettel-mysign' (raw provider) maps signer cert + alg for format='Raw'
// TC-DSIG-REMOTE-007: Pkcs7 providers never carry signerCertBase64 (verify stays on CMS branch)

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { uint8ToBase64 } from '@/features/digital-signature/base64-utils';

// ─── Mock signature-api before adapter imports it ─────────────────────────────
// vi.mock() is hoisted to top of file by vitest, so mockCallSign must be
// initialised via vi.hoisted() to be accessible inside the factory function.

const { mockCallSign } = vi.hoisted(() => ({ mockCallSign: vi.fn() }));

vi.mock('@/features/digital-signature/signature-api', () => ({
  callSign: mockCallSign,
  callVerify: vi.fn(),
  useSignatureConfig: vi.fn(),
  useAvailableProviders: vi.fn(),
  useUpdateSignatureConfig: vi.fn(),
  signatureQueryKeys: { all: [], config: () => [], providers: () => [] },
}));

// Import adapter AFTER mock is in place so it picks up the mocked callSign
import '@/features/digital-signature/providers/remote-signing-adapter';
import { resolveAdapter, hasAdapter } from '@/features/digital-signature/registry';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makePayload(text: string): Uint8Array {
  return new TextEncoder().encode(text);
}

// TC-DSIG-REMOTE-001
describe('remote-signing-adapter — TC-DSIG-REMOTE-001: calls callSign with correct args', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Mock returns updated BE shape: { isSuccess, isPending, signature (not signatureBase64) }
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: false,
      signature: 'sig==',
      transactionId: null,
      failureReason: null,
      signingCertificate: null,
    });
  });

  it('sends payload field (not payloadBase64) as chunked base64 of payload bytes', async () => {
    const payload = makePayload('hello world');
    // Use same chunked encoder the adapter uses — ensures exact match
    const expectedBase64 = uint8ToBase64(payload);

    const adapter = resolveAdapter('remote-vnpt-smartca');
    await adapter.sign(payload, { reason: 'test reason' });

    expect(mockCallSign).toHaveBeenCalledWith(
      expect.objectContaining({
        payload: expectedBase64,   // BE field: payload (not payloadBase64)
        reason: 'test reason',
      }),
    );
  });

  it('omits reason when not provided', async () => {
    const adapter = resolveAdapter('remote-vnpt-smartca');
    await adapter.sign(makePayload('data'), {});

    expect(mockCallSign).toHaveBeenCalledWith(
      expect.objectContaining({ reason: undefined }),
    );
  });
});

// TC-DSIG-REMOTE-002
describe('remote-signing-adapter — TC-DSIG-REMOTE-002: maps response to SignResult', () => {
  it('returns correct SignResult from successful response', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: false,
      signature: 'ABCD==',      // BE field name: signature (byte[] → base64)
      transactionId: 'tx-123',
      failureReason: null,
      signingCertificate: null,
    });

    const adapter = resolveAdapter('remote-vnpt-smartca');
    const result = await adapter.sign(makePayload('test'), {});

    expect(result).toEqual({
      signatureBase64: 'ABCD==',         // FE internal name
      providerKey: 'remote-vnpt-smartca',
      isPending: false,
      transactionId: 'tx-123',
    });
  });

  it('sets isPending true for async approval flow (VNPT SmartCA mobile)', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: true,
      signature: null,           // null when pending — caller must poll confirm endpoint
      transactionId: 'pending-tx',
      failureReason: null,
      signingCertificate: null,
    });

    const adapter = resolveAdapter('remote-vnpt-smartca');
    const result = await adapter.sign(makePayload('doc'), { reason: 'approval' });

    expect(result.isPending).toBe(true);
    expect(result.transactionId).toBe('pending-tx');
    // signatureBase64 is '' (empty) when pending — caller checks isPending first
    expect(result.signatureBase64).toBe('');
  });
});

// TC-DSIG-REMOTE-003
describe('remote-signing-adapter — TC-DSIG-REMOTE-003: propagates callSign rejection', () => {
  it('rejects when callSign throws', async () => {
    mockCallSign.mockRejectedValue(new Error('Network error'));

    const adapter = resolveAdapter('remote-vnpt-smartca');
    await expect(adapter.sign(makePayload('fail'), {})).rejects.toThrow('Network error');
  });
});

// TC-DSIG-REMOTE-004
describe('remote-signing-adapter — TC-DSIG-REMOTE-004: throws on isSuccess=false', () => {
  it('rejects with failureReason when BE reports failure', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: false,
      isPending: false,
      signature: null,
      transactionId: null,
      failureReason: 'Provider unavailable',
      signingCertificate: null,
    });

    const adapter = resolveAdapter('remote-vnpt-smartca');
    await expect(adapter.sign(makePayload('test'), {})).rejects.toThrow('Provider unavailable');
  });

  it('uses fallback message when failureReason is null', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: false,
      isPending: false,
      signature: null,
      transactionId: null,
      failureReason: null,
      signingCertificate: null,
    });

    const adapter = resolveAdapter('remote-vnpt-smartca');
    await expect(adapter.sign(makePayload('test'), {})).rejects.toThrow('Remote signing failed');
  });
});

// TC-DSIG-REMOTE-005
describe('remote-signing-adapter — TC-DSIG-REMOTE-005: fallback "remote" key is registered', () => {
  it('hasAdapter returns true for "remote" key', () => {
    expect(hasAdapter('remote')).toBe(true);
  });

  it('"remote" adapter can sign without crashing', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: false,
      signature: 'fallback==',
      transactionId: null,
      failureReason: null,
      signingCertificate: null,
    });

    const adapter = resolveAdapter('remote');
    const result = await adapter.sign(makePayload('test'), {});
    expect(result.signatureBase64).toBe('fallback==');
    expect(result.providerKey).toBe('remote');
  });
});

// TC-DSIG-REMOTE-006
describe('remote-signing-adapter — TC-DSIG-REMOTE-006: Viettel Mysign raw provider maps signer cert', () => {
  it('"remote-viettel-mysign" key is registered (D19 active provider)', () => {
    expect(hasAdapter('remote-viettel-mysign')).toBe(true);
  });

  it('maps signingCertificate.rawData → signerCertBase64 + SHA256/RSA so verify sends format=Raw', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: false,
      signature: 'RAWSIG==',
      transactionId: 'vt-tx-1',
      failureReason: null,
      // BE Viettel sync response: signer cert DER (base64) in signingCertificate.rawData
      signingCertificate: {
        serialNumber: 'AA11',
        subject: 'CN=Nguyen Van A',
        issuer: 'CN=Viettel CA',
        notBefore: '2026-01-01T00:00:00Z',
        notAfter: '2027-01-01T00:00:00Z',
        thumbprint: 'BEEF',
        rawData: 'Q0VSVERFUg==', // base64 DER signer cert
      },
    });

    const adapter = resolveAdapter('remote-viettel-mysign');
    const result = await adapter.sign(makePayload('doc'), { reason: 'demo' });

    expect(result).toEqual({
      signatureBase64: 'RAWSIG==',
      providerKey: 'remote-viettel-mysign',
      isPending: false,
      transactionId: 'vt-tx-1',
      signerCertBase64: 'Q0VSVERFUg==', // drives demo isRaw=true → format='Raw'
      hashAlg: 'SHA256',
      encode: 'RSA',
    });
  });

  it('omits signer cert when a sign is pending (no cert yet)', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: true,
      signature: null,
      transactionId: 'vt-pending',
      failureReason: null,
      signingCertificate: null,
    });

    const adapter = resolveAdapter('remote-viettel-mysign');
    const result = await adapter.sign(makePayload('doc'), {});

    expect(result.isPending).toBe(true);
    expect(result.signerCertBase64).toBeUndefined();
    expect(result.hashAlg).toBeUndefined();
  });
});

// TC-DSIG-REMOTE-007
describe('remote-signing-adapter — TC-DSIG-REMOTE-007: Pkcs7 providers never carry signer cert', () => {
  it('VNPT SmartCA ignores signingCertificate even if BE populates it (CMS embeds its own cert)', async () => {
    mockCallSign.mockResolvedValue({
      isSuccess: true,
      isPending: false,
      signature: 'CMS==',
      transactionId: null,
      failureReason: null,
      // Even if a cert is present, a Pkcs7 provider must NOT flip the verify path to Raw.
      signingCertificate: {
        serialNumber: 'BB22',
        subject: 'CN=SmartCA User',
        issuer: 'CN=VNPT CA',
        notBefore: '2026-01-01T00:00:00Z',
        notAfter: '2027-01-01T00:00:00Z',
        thumbprint: 'CAFE',
        rawData: 'U01BUlRDQQ==',
      },
    });

    const adapter = resolveAdapter('remote-vnpt-smartca');
    const result = await adapter.sign(makePayload('test'), {});

    expect(result.signerCertBase64).toBeUndefined();
    expect(result.hashAlg).toBeUndefined();
    expect(result.encode).toBeUndefined();
  });
});
