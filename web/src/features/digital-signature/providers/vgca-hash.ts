/**
 * vgca-hash.ts — SHA-256 helper for VGCA USB token adapter.
 *
 * VGCA vgca_sign_msg requires the caller to pre-compute the SHA-256 hash of the payload
 * and supply it as base64 in the HashValue field. This module provides that utility
 * using the browser's native crypto.subtle API.
 */

import { uint8ToBase64 } from '../base64-utils';

/**
 * Compute SHA-256 of a binary payload and return it as a base64 string.
 * This is the HashValue expected by VGCA vgca_sign_msg:
 *   JSON.stringify({ HashAlg: 'SHA256', HashValue: await sha256Base64(payload), Encode: 'RSA' })
 *
 * @param payload - The raw binary payload to hash.
 * @returns Base64-encoded SHA-256 digest.
 * @throws If called in a non-browser environment without crypto.subtle.
 */
export async function sha256Base64(payload: Uint8Array): Promise<string> {
  if (typeof crypto === 'undefined' || !crypto.subtle) {
    throw new Error(
      'vgca-hash requires browser crypto.subtle — not available in this environment (SSR or non-HTTPS context)',
    );
  }

  // Cast to BufferSource via ArrayBuffer view to satisfy TS lib types
  // (Uint8Array<SharedArrayBuffer> vs Uint8Array<ArrayBuffer> nominal divergence in TS5+).
  const hashBuffer = await crypto.subtle.digest('SHA-256', payload as unknown as BufferSource);
  const hashBytes = new Uint8Array(hashBuffer);
  return uint8ToBase64(hashBytes);
}
