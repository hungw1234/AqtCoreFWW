import { Tag } from 'antd';

// Color presets — covers common entity lifecycle statuses across the app
const STATUS_COLOR_MAP: Record<string, string> = {
  // Generic lifecycle
  active: 'green',
  inactive: 'default',
  draft: 'default',
  pending: 'orange',
  approved: 'green',
  rejected: 'red',
  closed: 'default',
  cancelled: 'red',
  // Case statuses
  submitted: 'blue',
  underreview: 'processing',
  returnedforrevision: 'warning',
  // Job / process statuses
  running: 'processing',
  completed: 'green',
  failed: 'red',
  queued: 'orange',
  // Rule set statuses
  deprecated: 'red',
};

interface StatusBadgeProps {
  status: string;
  /** Override auto-derived label — defaults to capitalized status value */
  label?: string;
}

// StatusBadge — colored Tag for entity status strings; auto-maps common values
export function StatusBadge({ status, label }: StatusBadgeProps) {
  const key = status.toLowerCase().replace(/\s+/g, '');
  const color = STATUS_COLOR_MAP[key] ?? 'default';
  const text = label ?? status.charAt(0).toUpperCase() + status.slice(1);

  return <Tag color={color}>{text}</Tag>;
}
