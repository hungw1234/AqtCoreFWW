import { useEffect, useState } from 'react';
import { Select, Button, Space } from 'antd';
import { CheckOutlined } from '@ant-design/icons';

const { Option } = Select;

interface RoleSyncSelectProps {
  userId: string;
  currentRoles: string[];
  assignableRoles: string[];
  syncRolesMutation: {
    mutate: (data: { id: string; roles: string[] }, opts?: { onSuccess?: () => void; onError?: () => void }) => void;
    isPending: boolean;
  };
  onSuccess?: () => void;
  onError?: () => void;
}

/** Inline multi-select with explicit save button — avoids onBlur timing issues */
export function RoleSyncSelect({
  userId,
  currentRoles,
  assignableRoles,
  syncRolesMutation,
  onSuccess,
  onError,
}: RoleSyncSelectProps) {
  const [selected, setSelected] = useState<string[]>(currentRoles);

  // Sync local state when server data changes (e.g., after refetch)
  useEffect(() => {
    setSelected(currentRoles);
  }, [currentRoles]);

  const hasChanged = selected.length !== currentRoles.length ||
    selected.some((r) => !currentRoles.includes(r));

  function handleSave() {
    syncRolesMutation.mutate(
      { id: userId, roles: selected },
      { onSuccess, onError },
    );
  }

  return (
    <Space size={4}>
      <Select
        mode="multiple"
        size="small"
        style={{ minWidth: 160, maxWidth: 240 }}
        value={selected}
        loading={syncRolesMutation.isPending}
        onChange={setSelected}
      >
        {assignableRoles.map((r) => <Option key={r} value={r}>{r}</Option>)}
      </Select>
      {hasChanged && (
        <Button
          type="primary"
          size="small"
          icon={<CheckOutlined />}
          loading={syncRolesMutation.isPending}
          onClick={handleSave}
        />
      )}
    </Space>
  );
}
