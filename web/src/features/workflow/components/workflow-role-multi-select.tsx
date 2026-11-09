// RT2-15 Phase 03 — multi-role picker for ByRole clause mode in the transition assignment
// editor. Reuses /api/v1/admin/roles via useRoles() (existing typed hook). Role list is
// bounded (~10-50 active per tenant) so we fetch upfront and filter client-side via
// AntD Select showSearch — no debounce / min-char wrapper needed (mirror pattern from
// WorkflowUserMultiSelect is overkill here).
//
// Value semantic: array of role CODES (e.g. "BTC_MINISTER", stable ASCII) — mirrors the
// Permission/Group/OrgUnit code semantics. Save-time canonicalize Trim+ToUpperInvariant runs
// server-side in WorkflowGraphValidator.

import { Select } from 'antd';
import { useRoles } from '@/features/roles/roles-api';

export interface WorkflowRoleMultiSelectProps {
  value: string[];
  onChange: (next: string[]) => void;
  placeholder?: string;
  disabled?: boolean;
}

export function WorkflowRoleMultiSelect({
  value,
  onChange,
  placeholder,
  disabled = false,
}: WorkflowRoleMultiSelectProps) {
  const { data: roles, isLoading } = useRoles();

  const options = (roles ?? [])
    .filter((r) => r.isActive && !!r.code)
    .map((r) => ({
      label: r.description ? `${r.name} · ${r.description}` : r.name,
      value: r.code,
    }));

  return (
    <Select
      mode="multiple"
      showSearch
      value={value}
      onChange={onChange}
      filterOption={(input, option) =>
        (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
      }
      placeholder={placeholder}
      disabled={disabled}
      loading={isLoading}
      options={options}
      style={{ width: '100%' }}
    />
  );
}
