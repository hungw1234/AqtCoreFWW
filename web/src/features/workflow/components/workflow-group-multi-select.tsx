// RT2-15 Phase 02 — multi-group picker for ByGroup clause mode in the transition
// assignment editor. Mirrors workflow-role-multi-select.tsx: bounded list, full fetch
// upfront, client-side showSearch filter — no debounce needed (groups bounded per tenant).
//
// Value semantic: array of group CODES (canonical UPPER, e.g. "GROUP_TEST_A") per PQ-2.
// Save-time canonicalize Trim+ToUpperInvariant runs server-side in WorkflowGraphValidator.
//
// Item display: "{name} ({code})" — name is primary label, code in parens as muted context.
// Empty state shows when activeOnly=true returns an empty list.

import { Select } from 'antd';
import { useUserGroups } from '../hooks/use-user-groups';

export interface WorkflowGroupMultiSelectProps {
  value: string[];
  onChange: (next: string[]) => void;
  placeholder?: string;
  disabled?: boolean;
}

export function WorkflowGroupMultiSelect({
  value,
  onChange,
  placeholder,
  disabled = false,
}: WorkflowGroupMultiSelectProps) {
  const { data: groups, isLoading } = useUserGroups({ activeOnly: true });

  // Sort alphabetically by name (server returns Name ASC; client-side fallback for safety).
  const options = [...(groups ?? [])]
    .sort((a, b) => a.name.localeCompare(b.name, 'vi'))
    .map((g) => ({
      label: `${g.name} (${g.code})`,
      value: g.code,
    }));

  const notFoundContent = isLoading ? null : 'Không có nhóm hoạt động';

  return (
    <Select
      mode="multiple"
      showSearch
      value={value}
      onChange={onChange}
      filterOption={(input, option) =>
        (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
      }
      placeholder={placeholder ?? 'Chọn nhóm người dùng...'}
      disabled={disabled}
      loading={isLoading}
      options={options}
      notFoundContent={notFoundContent}
      style={{ width: '100%' }}
    />
  );
}
