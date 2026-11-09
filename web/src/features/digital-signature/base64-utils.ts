/**
 * base64-utils.ts — safe base64 encoding/decoding for binary payloads.
 *
 * Problem: `btoa(String.fromCharCode(...Array.from(bytes)))` uses spread operator
 * which passes all bytes as individual function arguments. JavaScript engines have
 * a stack argument limit (typically ~65536 args). PDFs and other real-world documents
 * easily exceed this — a 1MB PDF would cause a RangeError: Maximum call stack size exceeded.
 *
 * Fix: chunk the input into 32KB slices before calling String.fromCharCode.apply().
 * 32KB (0x8000) is well below the ~65K argument limit; avoids stack overflow on any
 * standards-compliant engine.
 */

/** Size of each chunk passed to String.fromCharCode.apply() */
const CHUNK_SIZE = 0x8000; // 32 KB

/**
 * Encode a Uint8Array to a base64 string without RangeError on large payloads.
 * Safe for payloads of any size (PDFs, binaries, etc.).
 *
 * @example
 * const b64 = uint8ToBase64(new Uint8Array([72, 101, 108, 108, 111])); // "SGVsbG8="
 */
export function uint8ToBase64(bytes: Uint8Array): string {
  let binary = '';
  for (let i = 0; i < bytes.length; i += CHUNK_SIZE) {
    const end = Math.min(i + CHUNK_SIZE, bytes.length);
    const chunk = bytes.subarray(i, end);
    // apply() distributes chunk as individual args — safe because chunk is ≤ CHUNK_SIZE
    binary += String.fromCharCode.apply(null, Array.from(chunk));
  }
  return btoa(binary);
}

/**
 * Decode a base64 string back to a Uint8Array.
 * Inverse of uint8ToBase64.
 */
export function base64ToUint8(b64: string): Uint8Array {
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}
