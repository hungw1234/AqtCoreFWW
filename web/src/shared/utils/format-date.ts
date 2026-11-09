import dayjs from 'dayjs';

// Date format constants — consistent across the GOV admin UI
export const DATE_FORMAT = 'DD/MM/YYYY';
export const DATETIME_FORMAT = 'DD/MM/YYYY HH:mm';

/**
 * Format an ISO date string or Date object to DD/MM/YYYY.
 * Returns '—' for null/undefined/invalid input.
 */
export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const d = dayjs(value);
  return d.isValid() ? d.format(DATE_FORMAT) : '—';
}

/**
 * Format an ISO date string or Date object to DD/MM/YYYY HH:mm.
 * Returns '—' for null/undefined/invalid input.
 */
export function formatDateTime(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const d = dayjs(value);
  return d.isValid() ? d.format(DATETIME_FORMAT) : '—';
}

/**
 * Format a UTC ISO string to local DD/MM/YYYY HH:mm.
 * Alias of formatDateTime — named explicitly for clarity at call sites.
 */
export function formatUtcDateTime(value: string | null | undefined): string {
  return formatDateTime(value);
}
