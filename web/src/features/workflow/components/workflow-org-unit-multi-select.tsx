// RT2-15 Phase 04 — multi-org-unit picker for ByOrgUnit clause mode in the transition
// assignment editor. Mirrors workflow-group-multi-select.tsx: bounded list, full fetch
// upfront, client-side showSearch filter — no debounce needed (org units bounded per tenant).
//
// Value semantic: array of org unit CODES (canonical UPPER, e.g. "OU_PHONG_TIEP_NHAN") per PQ-3.
// Save-time canonicalize Trim+ToUpperInvariant runs server-side in WorkflowGraphValidator.
//
// Item display: "{name} ({code})" — name is primary label, code in parens as muted context.
// Empty state shown when activeOnly=true returns an empty list.

import { Select } from 'antd';
import { useWorkflowOrgUnits } from '../hooks/use-org-units';

export interface WorkflowOrgUnitMultiSelectProps {
  value: string[];
  onChange: (next: string[]) => void;
  placeholder?: string;
  disabled?: boolean;
}

export function WorkflowOrgUnitMultiSelect({
  value,
  onChange,
  placeholder,
  disabled = false,
}: WorkflowOrgUnitMultiSelectProps) {
  const { data: orgUnits, isLoading } = useWorkflowOrgUnits({ activeOnly: true });

  // Filter to active org units only (BE returns full tree; activeOnly param not bound by controller).
  // Sort alphabetically by name (server returns Name ASC; client-side fallback for safety).
  const options = [...(orgUnits ?? [])]
    .filter((ou) => ou.isActive)
    .sort((a, b) => a.name.localeCompare(b.name, 'vi'))
    .map((ou) => ({
      label: `${ou.name} (${ou.code})`,
      value: ou.code,
    }));

  const notFoundContent = isLoading ? null : 'Không có đơn vị tổ chức hoạt động';

  return (
    <Select
      mode="multiple"
      showSearch
      value={value}
      onChange={onChange}
      filterOption={(input, option) =>
        (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
      }
      placeholder={placeholder ?? 'Chọn đơn vị tổ chức...'}
      disabled={disabled}
      loading={isLoading}
      options={options}
      notFoundContent={notFoundContent}
      style={{ width: '100%' }}
    />
  );
}
