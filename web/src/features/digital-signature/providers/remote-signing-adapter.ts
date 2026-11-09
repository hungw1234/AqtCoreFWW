/**
 * remote-signing-adapter.ts — Remote signing adapter (Viettel Mysign + other cloud providers).
 *
 * Routes signing through the BE /api/v1/digital-signature/sign endpoint.
 * The BE resolver selects the active tenant provider by key. Valid keys (D19 contract,
 * TenantSignatureConfig): "remote-viettel-mysign" (active) and the deprecated "remote-vnpt-smartca".
 * Remote providers may return isPending=true for async approval flows (mobile OTP).
 *
 * Two signature formats flow through here:
 *  - Raw  (Viettel Mysign): server-side signHash returns a bare RSA PKCS#1 v1.5 signature plus
 *    the signer cert separately. The verify path keys off signerCertBase64 to send format='Raw';
 *    omitting it makes the BE default to Pkcs7 and route the bare signature into the CMS parser
 *    ("CMS signature could not be parsed").
 *  - Pkcs7 (VNPT SmartCA / generic): CMS envelope that embeds its own cert — no separate cert.
 *
 * Uses callSign() indirection from signature-api.ts — swap to real fetch in one file.
 */

import { registerAdapter } from '../registry';
import { callSign } from '../signature-api';
import { uint8ToBase64 } from '../base64-utils';
import type { SignOpts, SignResult } from '../signature-types';

// ─── Shared sign implementation ───────────────────────────────────────────────

async function remoteSign(
  providerKey: string,
  payload: Uint8Array,
  opts: SignOpts,
  // True only for providers whose signature is bare RSA/ECDSA with a separately
  // returned signer cert (Viettel Mysign). CMS providers leave this false so the
  // verify path stays on the Pkcs7 branch.
  isRawProvider = false,
): Promise<SignResult> {
  // Encode raw bytes to base64 for transport — use chunked encoder, not
  // spread-based btoa(), which throws RangeError on payloads > ~64KB.
  const payloadBase64 = uint8ToBase64(payload);

  // BE field: payload (not payloadBase64) per Presentation.SignRequest shape
  const response = await callSign({
    payload: payloadBase64,
    reason: opts.reason,
  });

  if (!response.isSuccess && !response.isPending) {
    throw new Error(response.failureReason ?? 'Remote signing failed');
  }

  const result: SignResult = {
    // BE field: signature (not signatureBase64) — base64-encoded byte[]
    signatureBase64: response.signature ?? '',
    providerKey,
    isPending: response.isPending,
    transactionId: response.transactionId ?? undefined,
  };

  // Carry the signer cert + alg metadata for raw providers so the verify request
  // can declare format='Raw'. The BE populates signingCertificate.rawData (DER,
  // base64) for Viettel sync responses; it is null while a sign is pending.
  if (isRawProvider && response.signingCertificate?.rawData) {
    result.signerCertBase64 = response.signingCertificate.rawData;
    result.hashAlg = 'SHA256'; // Viettel Mysign signHash default OID
    result.encode = 'RSA'; // raw RSA PKCS#1 v1.5
  }

  return result;
}

// ─── Register adapters for each remote provider key ───────────────────────────

// Viettel Mysign — active remote provider (D19). Server-side signHash → raw RSA + separate cert.
registerAdapter('remote-viettel-mysign', {
  sign: (payload: Uint8Array, opts: SignOpts): Promise<SignResult> =>
    remoteSign('remote-viettel-mysign', payload, opts, true),
});

// VNPT SmartCA — deprecated post-D19; CMS/Pkcs7 envelope (embeds its own cert, no raw mapping).
registerAdapter('remote-vnpt-smartca', {
  sign: (payload: Uint8Array, opts: SignOpts): Promise<SignResult> =>
    remoteSign('remote-vnpt-smartca', payload, opts),
});

// Generic remote fallback — covers any future cloud providers added to BE without FE changes
registerAdapter('remote', {
  sign: (payload: Uint8Array, opts: SignOpts): Promise<SignResult> =>
    remoteSign('remote', payload, opts),
});
