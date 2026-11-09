// Plan 260523-1241 Phase 06b — multi-user picker for SpecificUser clause mode in the
// transition assignment editor. Wraps collaboration/user-search-api (existing backend
// GET /api/v1/users/search returning minimal fields) with AntD Select multi-select +
// debounced query (300 ms, min 2 chars) + showSearch.
//
// Contract:
//   value: array of selected user ids (Guid strings)
//   onChange: callback with new array on each selection change
//   placeholder: optional placeholder text
//
// Why a focused wrapper instead of generic AntD Select:
//   - Encapsulates debounce + min-char-gate so call-sites cannot accidentally hammer the search endpoint.
//   - Keeps query state internal; parent only owns the selected-ids array.
//   - Centralises label rendering (fullName + optional departmentCode badge) per UX convention.

import { useState, useEffect } from 'react';
import { Select } from 'antd';
import { useUserSearch } from '@/features/collaboration/user-search-api';

const MIN_QUERY_CHARS = 2;
const DEBOUNCE_MS = 300;

export interface WorkflowUserMultiSelectProps {
  value: string[];
  onChange: (next: string[]) => void;
  placeholder?: string;
  disabled?: boolean;
}

export function WorkflowUserMultiSelect({
  value,
  onChange,
  placeholder,
  disabled = false,
}: WorkflowUserMultiSelectProps) {
  const [rawQuery, setRawQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedQuery(rawQuery), DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [rawQuery]);

  const enabled = debouncedQuery.length >= MIN_QUERY_CHARS;
  const { data: results, isLoading } = useUserSearch(debouncedQuery, enabled);

  const options = (results ?? []).map((u) => ({
    label: u.departmentCode ? `${u.fullName} · ${u.departmentCode}` : u.fullName,
    value: u.id,
  }));

  return (
    <Select
      mode="multiple"
      showSearch
      value={value}
      onChange={onChange}
      onSearch={setRawQuery}
      filterOption={false}
      placeholder={placeholder}
      disabled={disabled}
      loading={isLoading}
      options={options}
      style={{ width: '100%' }}
    />
  );
}
