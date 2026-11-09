/**
 * uint8-to-base64.test.ts — unit tests for base64-utils chunked encoder.
 *
 * Covers:
 *   - Correctness: known byte sequences encode to expected base64 strings
 *   - Large payload: 5 MB buffer encodes without RangeError (stack overflow guard)
 *   - Round-trip: uint8ToBase64 → base64ToUint8 recovers original bytes exactly
 *   - hasCapability: comma-separated capability string parsing
 */

import { describe, it, expect } from 'vitest';
import { uint8ToBase64, base64ToUint8 } from '@/features/digital-signature/base64-utils';
import { hasCapability } from '@/features/digital-signature/signature-types';

// ─── uint8ToBase64 ────────────────────────────────────────────────────────────

describe('uint8ToBase64', () => {
  it('encodes empty buffer to empty string', () => {
    expect(uint8ToBase64(new Uint8Array([]))).toBe('');
  });

  it('encodes known bytes correctly', () => {
    // "Hello" in ASCII = [72,101,108,108,111] → base64 "SGVsbG8="
    const bytes = new Uint8Array([72, 101, 108, 108, 111]);
    expect(uint8ToBase64(bytes)).toBe('SGVsbG8=');
  });

  it('encodes single byte correctly', () => {
    // 0x00 → "AA=="
    expect(uint8ToBase64(new Uint8Array([0]))).toBe('AA==');
  });

  it('encodes two bytes correctly', () => {
    // [0, 0] → "AAA="
    expect(uint8ToBase64(new Uint8Array([0, 0]))).toBe('AAA=');
  });

  it('matches btoa for a small payload', () => {
    const bytes = new Uint8Array(256);
    for (let i = 0; i < 256; i++) bytes[i] = i;
    // btoa reference — safe here because payload is only 256 bytes
    const expected = btoa(String.fromCharCode(...Array.from(bytes)));
    expect(uint8ToBase64(bytes)).toBe(expected);
  });

  it('handles 5 MB payload without RangeError', () => {
    // 5 MB = 5 * 1024 * 1024 bytes — well above the ~64K spread argument limit
    const size = 5 * 1024 * 1024;
    const bytes = new Uint8Array(size);
    // Fill with repeating pattern to make content non-trivial
    for (let i = 0; i < size; i++) bytes[i] = i & 0xff;

    let result: string;
    expect(() => {
      result = uint8ToBase64(bytes);
    }).not.toThrow();

    // Verify output is valid base64: only base64 alphabet + padding
    expect(result!).toMatch(/^[A-Za-z0-9+/]*={0,2}$/);
    // Length should be ceil(5MB / 3) * 4
    const expectedLength = Math.ceil(size / 3) * 4;
    expect(result!.length).toBe(expectedLength);
  });
});

// ─── Round-trip ───────────────────────────────────────────────────────────────

describe('uint8ToBase64 / base64ToUint8 round-trip', () => {
  it('recovers original bytes for a small buffer', () => {
    const original = new Uint8Array([1, 2, 3, 255, 0, 127, 128]);
    const b64 = uint8ToBase64(original);
    const recovered = base64ToUint8(b64);
    expect(recovered).toEqual(original);
  });

  it('recovers original bytes for a 1 MB buffer', () => {
    const size = 1024 * 1024;
    const original = new Uint8Array(size);
    for (let i = 0; i < size; i++) original[i] = (i * 31 + 7) & 0xff; // pseudo-random
    const b64 = uint8ToBase64(original);
    const recovered = base64ToUint8(b64);
    expect(recovered).toEqual(original);
  });
});

// ─── hasCapability ────────────────────────────────────────────────────────────

describe('hasCapability', () => {
  it('returns true for exact match', () => {
    expect(hasCapability('RequiresLocalAgent', 'RequiresLocalAgent')).toBe(true);
  });

  it('returns true when capability is in a comma-separated list', () => {
    expect(hasCapability('ServerSideSigning, RequiresLocalAgent', 'RequiresLocalAgent')).toBe(true);
    expect(hasCapability('ServerSideSigning, RequiresLocalAgent', 'ServerSideSigning')).toBe(true);
  });

  it('returns false when capability is absent', () => {
    expect(hasCapability('ServerSideSigning, BatchSigning', 'RequiresLocalAgent')).toBe(false);
  });

  it('returns false for empty string', () => {
    expect(hasCapability('', 'RequiresLocalAgent')).toBe(false);
  });

  it('handles None capability', () => {
    expect(hasCapability('None', 'None')).toBe(true);
    expect(hasCapability('None', 'ServerSideSigning')).toBe(false);
  });

  it('handles extra whitespace around entries', () => {
    // BE may produce "ServerSideSigning,  RequiresLocalAgent" with varying spaces
    expect(hasCapability('ServerSideSigning,  RequiresLocalAgent', 'RequiresLocalAgent')).toBe(true);
  });
});
