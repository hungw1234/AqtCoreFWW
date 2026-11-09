import type { AuditLogEntry } from './audit-types';

// CSV column definitions — label + field accessor
const CSV_COLUMNS: { label: string; key: keyof AuditLogEntry }[] = [
  { label: 'Thời gian', key: 'occurredAt' },
  { label: 'Người dùng', key: 'userName' },
  { label: 'Hành động', key: 'action' },
  { label: 'Module', key: 'moduleName' },
  { label: 'Loại tài nguyên', key: 'resourceType' },
  { label: 'ID tài nguyên', key: 'resourceId' },
  { label: 'Địa chỉ IP', key: 'ipAddress' },
  { label: 'Correlation ID', key: 'correlationId' },
];

// Escape a CSV cell value — wrap in quotes if it contains commas, quotes, or newlines
function escapeCsvCell(value: unknown): string {
  const str = value == null ? '' : String(value);
  if (/[",\n\r]/.test(str)) {
    return `"${str.replace(/"/g, '""')}"`;
  }
  return str;
}

// Convert audit log entries to a CSV string and trigger browser download
export function exportAuditLogsToCsv(entries: AuditLogEntry[], filename = 'audit-logs.csv'): void {
  const header = CSV_COLUMNS.map((c) => c.label).join(',');
  const rows = entries.map((entry) =>
    CSV_COLUMNS.map((c) => escapeCsvCell(entry[c.key])).join(','),
  );

  const csv = [header, ...rows].join('\r\n');
  const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);

  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.click();

  URL.revokeObjectURL(url);
}
