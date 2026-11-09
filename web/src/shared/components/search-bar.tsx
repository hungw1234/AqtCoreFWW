import { Input } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';
import { useState, useEffect } from 'react';

const { Search } = Input;

interface SearchBarProps {
  /** Called with debounced value after user stops typing */
  onSearch: (value: string) => void;
  placeholder?: string;
  debounceMs?: number;
  /** Width in pixels or CSS string (default 320) */
  width?: number | string;
  loading?: boolean;
}

// SearchBar — controlled input with configurable debounce, fires onSearch after delay
export function SearchBar({
  onSearch,
  placeholder = 'Search...',
  debounceMs = 300,
  width = 320,
  loading = false,
}: SearchBarProps) {
  const [raw, setRaw] = useState('');
  const debounced = useDebouncedValue(raw, debounceMs);

  // Fire callback whenever debounced value changes
  useEffect(() => {
    onSearch(debounced);
  }, [debounced, onSearch]);

  return (
    <Search
      prefix={<SearchOutlined />}
      placeholder={placeholder}
      value={raw}
      onChange={(e) => setRaw(e.target.value)}
      allowClear
      loading={loading}
      style={{ width }}
    />
  );
}
