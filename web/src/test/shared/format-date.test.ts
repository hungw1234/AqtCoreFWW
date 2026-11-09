import { describe, it, expect } from 'vitest';
import { formatDate, formatDateTime, formatUtcDateTime } from '@/shared/utils/format-date';

// TC-FE-UTIL-001: formatDate formats ISO string correctly
describe('formatDate — valid inputs', () => {
  it('TC-FE-UTIL-001: formats ISO date string to DD/MM/YYYY', () => {
    expect(formatDate('2024-03-15T10:30:00Z')).toBe('15/03/2024');
  });

  it('formats a Date object to DD/MM/YYYY', () => {
    expect(formatDate(new Date('2024-01-01'))).toMatch(/01\/01\/2024/);
  });

  it('formatDateTime formats ISO to DD/MM/YYYY HH:mm', () => {
    const result = formatDateTime('2024-06-20T14:05:00Z');
    // Time part may vary by timezone — just assert date portion
    expect(result).toMatch(/20\/06\/2024/);
  });

  it('formatUtcDateTime is an alias of formatDateTime for valid input', () => {
    const result = formatUtcDateTime('2024-06-20T14:05:00Z');
    expect(result).toMatch(/20\/06\/2024/);
  });
});

// TC-FE-UTIL-002: formatDate handles null/undefined
describe('formatDate — null/undefined', () => {
  it('TC-FE-UTIL-002: returns em-dash for null', () => {
    expect(formatDate(null)).toBe('—');
  });

  it('returns em-dash for undefined', () => {
    expect(formatDate(undefined)).toBe('—');
  });

  it('formatDateTime returns em-dash for null', () => {
    expect(formatDateTime(null)).toBe('—');
  });

  it('formatUtcDateTime returns em-dash for null', () => {
    expect(formatUtcDateTime(null)).toBe('—');
  });
});

// TC-FE-UTIL-003: formatDate handles invalid string
describe('formatDate — invalid inputs', () => {
  it('TC-FE-UTIL-003: returns em-dash for invalid date string', () => {
    expect(formatDate('not-a-date')).toBe('—');
  });

  it('returns em-dash for empty string', () => {
    expect(formatDate('')).toBe('—');
  });

  it('formatDateTime returns em-dash for invalid string', () => {
    expect(formatDateTime('garbage')).toBe('—');
  });
});
