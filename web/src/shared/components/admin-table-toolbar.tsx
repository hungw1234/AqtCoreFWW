import { Flex, Input, Select } from 'antd';

// FilterConfig — defines a single dropdown filter in the toolbar
interface FilterConfig {
  key: string;
  placeholder: string;
  options: { label: string; value: string }[];
  width?: number; // default 160
}

interface AdminTableToolbarProps {
  searchPlaceholder?: string;
  searchValue?: string;
  onSearchChange?: (value: string) => void;
  /** Triggered on Enter key or search button click — use for submit-based search */
  onSearch?: (value: string) => void;
  filters?: FilterConfig[];
  filterValues?: Record<string, string | undefined>;
  onFilterChange?: (key: string, value: string | undefined) => void;
  actions?: React.ReactNode;
}

// AdminTableToolbar — search input + filter selects + action slot.
// Debounce is EXTERNAL: consumer manages with useDebouncedValue or similar.
// Actions are right-aligned via marginLeft: 'auto' on the actions container.
export function AdminTableToolbar({
  searchPlaceholder = 'Search...',
  searchValue,
  onSearchChange,
  onSearch,
  filters,
  filterValues,
  onFilterChange,
  actions,
}: AdminTableToolbarProps) {
  return (
    <Flex
      wrap
      gap={8}
      align="center"
      className="admin-table-toolbar"
      style={{ padding: '12px 24px' }}
    >
      {/* Search input — submit on Enter or button click */}
      <Input.Search
        placeholder={searchPlaceholder}
        value={searchValue}
        onChange={(e) => onSearchChange?.(e.target.value)}
        onSearch={(v) => onSearch?.(v)}
        allowClear
        enterButton
        style={{ width: 300 }}
      />

      {/* Dynamic filter selects */}
      {filters?.map((filter) => (
        <Select
          key={filter.key}
          allowClear
          placeholder={filter.placeholder}
          value={filterValues?.[filter.key] ?? undefined}
          onChange={(value) => onFilterChange?.(filter.key, value as string | undefined)}
          options={filter.options}
          style={{ width: filter.width ?? 160 }}
        />
      ))}

      {/* Action buttons — right-aligned */}
      {actions && (
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8, alignItems: 'center' }}>
          {actions}
        </div>
      )}
    </Flex>
  );
}
