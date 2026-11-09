/**
 * vgca-hash.test.ts — unit tests for sha256Base64 helper.
 *
 * Known test vector: SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
 * Base64 of those 32 bytes = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU="
 */

import { describe, expect, it } from 'vitest';
import { sha256Base64 } from '@/features/digital-signature/providers/vgca-hash';

describe('sha256Base64', () => {
  it('empty payload produces known SHA-256 base64 vector', async () => {
    // SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
    // Base64:      47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=
    const result = await sha256Base64(new Uint8Array(0));
    expect(result).toBe('47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=');
  });

  it('round-trip: hash of same input is stable across two calls', async () => {
    const payload = new TextEncoder().encode('hello vgca');
    const first = await sha256Base64(payload);
    const second = await sha256Base64(payload);
    expect(first).toBe(second);
    expect(first.length).toBeGreaterThan(0);
    // SHA-256 produces 32 bytes → base64 is always 44 chars (with padding)
    expect(first.length).toBe(44);
  });

  it('different inputs produce different hashes', async () => {
    const a = await sha256Base64(new TextEncoder().encode('payload-a'));
    const b = await sha256Base64(new TextEncoder().encode('payload-b'));
    expect(a).not.toBe(b);
  });

  it('result is valid base64 (no whitespace, correct charset)', async () => {
    const payload = new Uint8Array([0x00, 0xff, 0x80, 0x42]);
    const result = await sha256Base64(payload);
    // Base64 charset: A-Z a-z 0-9 + / and = padding
    expect(result).toMatch(/^[A-Za-z0-9+/]+=*$/);
  });

  it('sha256("a") produces known test vector', async () => {
    // W7: verify known vector for single-char input.
    // SHA-256("a") = ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb
    // Base64: "ypeBEsobvcr6wjGzmiPcTaeG7/gUfE5yuYB3ha/uSLs="
    const result = await sha256Base64(new TextEncoder().encode('a'));
    expect(result).toBe('ypeBEsobvcr6wjGzmiPcTaeG7/gUfE5yuYB3ha/uSLs=');
  });

  it('large payload (1MB) hashes without memory issues', async () => {
    // W7: verify the hash function can handle large payloads without hanging or OOM.
    const largePayload = new Uint8Array(1024 * 1024); // 1MB
    // Fill with pseudo-random pattern
    for (let i = 0; i < largePayload.length; i++) {
      largePayload[i] = (i * 7) % 256;
    }

    const result = await sha256Base64(largePayload);
    // Just verify we got a valid result (length + charset)
    expect(result.length).toBe(44);
    expect(result).toMatch(/^[A-Za-z0-9+/]+=*$/);
  });
});
