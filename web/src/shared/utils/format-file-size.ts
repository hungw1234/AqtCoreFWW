// Byte size thresholds
const KB = 1024;
const MB = 1024 * KB;
const GB = 1024 * MB;

/**
 * Convert a byte count into a human-readable string.
 * Examples:
 *   500        → "500 B"
 *   1536       → "1.5 KB"
 *   2097152    → "2.0 MB"
 *   1073741824 → "1.0 GB"
 */
export function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < KB) return `${bytes} B`;
  if (bytes < MB) return `${(bytes / KB).toFixed(1)} KB`;
  if (bytes < GB) return `${(bytes / MB).toFixed(1)} MB`;
  return `${(bytes / GB).toFixed(1)} GB`;
}
