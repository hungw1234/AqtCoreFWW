import { describe, it, expect } from 'vitest';
import { formatFileSize } from '@/shared/utils/format-file-size';

// TC-FE-UTIL-004: formatFileSize bytes→KB→MB→GB conversion
describe('formatFileSize — unit conversions', () => {
  it('TC-FE-UTIL-004a: formats bytes under 1 KB as "N B"', () => {
    expect(formatFileSize(500)).toBe('500 B');
    expect(formatFileSize(0)).toBe('0 B');
    expect(formatFileSize(1023)).toBe('1023 B');
  });

  it('TC-FE-UTIL-004b: formats bytes in KB range as "N.N KB"', () => {
    expect(formatFileSize(1024)).toBe('1.0 KB');
    expect(formatFileSize(1536)).toBe('1.5 KB');
    expect(formatFileSize(1024 * 1023)).toBe('1023.0 KB');
  });

  it('TC-FE-UTIL-004c: formats bytes in MB range as "N.N MB"', () => {
    expect(formatFileSize(1024 * 1024)).toBe('1.0 MB');
    expect(formatFileSize(2 * 1024 * 1024)).toBe('2.0 MB');
    expect(formatFileSize(1.5 * 1024 * 1024)).toBe('1.5 MB');
  });

  it('TC-FE-UTIL-004d: formats bytes in GB range as "N.N GB"', () => {
    expect(formatFileSize(1024 * 1024 * 1024)).toBe('1.0 GB');
    expect(formatFileSize(2.5 * 1024 * 1024 * 1024)).toBe('2.5 GB');
  });
});

// TC-FE-UTIL-005: formatFileSize handles negative/NaN
describe('formatFileSize — edge/invalid inputs', () => {
  it('TC-FE-UTIL-005a: returns em-dash for negative numbers', () => {
    expect(formatFileSize(-1)).toBe('—');
    expect(formatFileSize(-1024)).toBe('—');
  });

  it('TC-FE-UTIL-005b: returns em-dash for NaN', () => {
    expect(formatFileSize(NaN)).toBe('—');
  });

  it('TC-FE-UTIL-005c: returns em-dash for Infinity', () => {
    expect(formatFileSize(Infinity)).toBe('—');
    expect(formatFileSize(-Infinity)).toBe('—');
  });
});
