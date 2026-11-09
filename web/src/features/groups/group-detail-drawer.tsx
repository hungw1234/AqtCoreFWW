import { useState } from 'react';
import {
  Drawer, Tabs, Table, Button, Space, Tag, Select,
  Popconfirm, Spin, Typography,
} from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import {
  useGroup,
  useAddGroupMember,
  useRemoveGroupMember,
  useAssignGroupRole,
  useRemoveGroupRole,
} from './groups-api';
import { useUsers } from '@/features/users/user-api';
import { apiClient } from '@/core/api';
import { useQuery } from '@tanstack/react-query';

const { Text } = Typography;

// Fetch available roles from BE — { id, name }[]
interface RoleDto {
  id: string;
  name: string;
}

function useRoles() {
  return useQuery({
    queryKey: ['roles'],
    queryFn: () => apiClient.get<RoleDto[]>('/admin/roles').then((r) => r.data),
  });
}

interface Props {
  groupId: string | null;
  onClose: () => void;
}

// GroupDetailDrawer — tabbed drawer for managing group members and role assignments.
// Uses searchable Select dropdowns instead of raw GUID input.
export function GroupDetailDrawer({ groupId, onClose }: Props) {
  const { t } = useTranslation();
  const [selectedUserId, setSelectedUserId] = useState<string | undefined>();
  const [selectedRoleId, setSelectedRoleId] = useState<string | undefined>();

  const { data: group, isLoading } = useGroup(groupId);
  const { data: usersResult } = useUsers({ pageNumber: 1, pageSize: 100 });
  const { data: roles = [] } = useRoles();

  const addMember    = useAddGroupMember();
  const removeMember = useRemoveGroupMember();
  const assignRole   = useAssignGroupRole();
  const removeRole   = useRemoveGroupRole();

  // Existing member user IDs — exclude from "add member" dropdown
  const existingMemberIds = new Set((group?.members ?? []).map((m) => m.userId));
  const existingRoleIds = new Set(group?.roleIds ?? []);

  // Build user options for Select — exclude already-added members
  const userOptions = (usersResult?.items ?? [])
    .filter((u) => !existingMemberIds.has(u.id))
    .map((u) => ({ value: u.id, label: `${u.fullName} (${u.email})` }));

  // Build role options — exclude already-assigned roles
  const roleOptions = roles
    .filter((r) => !existingRoleIds.has(r.id))
    .map((r) => ({ value: r.id, label: r.name }));

  const memberColumns: ColumnsType<{ userId: string; fullName: string; email: string }> = [
    { title: 'Họ tên', dataIndex: 'fullName', key: 'fullName', ellipsis: true },
    { title: 'Email', dataIndex: 'email', key: 'email', ellipsis: true },
    {
      title: '',
      key: 'actions',
      width: 60,
      render: (_, record) => (
        <Popconfirm
          title="Xóa thành viên?"
          onConfirm={() =>
            groupId && removeMember.mutate({ groupId, userId: record.userId })
          }
          okText={t('common.confirm', 'Xác nhận')}
          cancelText={t('common.cancel', 'Hủy')}
        >
          <Button danger size="small" icon={<DeleteOutlined />} />
        </Popconfirm>
      ),
    },
  ];

  const handleAddMember = () => {
    if (!groupId || !selectedUserId) return;
    addMember.mutate({ groupId, userId: selectedUserId }, {
      onSuccess: () => setSelectedUserId(undefined),
    });
  };

  const handleAssignRole = () => {
    if (!groupId || !selectedRoleId) return;
    assignRole.mutate({ groupId, roleId: selectedRoleId }, {
      onSuccess: () => setSelectedRoleId(undefined),
    });
  };

  // Resolve role name from ID for display
  const roleName = (roleId: string) => roles.find((r) => r.id === roleId)?.name ?? roleId;

  const tabItems = [
    {
      key: 'members',
      label: `Thành viên${group ? ` (${group.memberCount})` : ''}`,
      children: (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Space>
            <Select
              placeholder="Tìm người dùng..."
              value={selectedUserId}
              onChange={setSelectedUserId}
              options={userOptions}
              showSearch
              optionFilterProp="label"
              style={{ width: 320 }}
              allowClear
            />
            <Button
              type="primary"
              icon={<PlusOutlined />}
              loading={addMember.isPending}
              onClick={handleAddMember}
              disabled={!selectedUserId}
            >
              Thêm
            </Button>
          </Space>
          <Table
            rowKey="userId"
            columns={memberColumns}
            dataSource={group?.members ?? []}
            size="small"
            pagination={{ pageSize: 10 }}
          />
        </Space>
      ),
    },
    {
      key: 'roles',
      label: `Vai trò${group ? ` (${group.roleIds.length})` : ''}`,
      children: (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Space>
            <Select
              placeholder="Chọn vai trò..."
              value={selectedRoleId}
              onChange={setSelectedRoleId}
              options={roleOptions}
              showSearch
              optionFilterProp="label"
              style={{ width: 320 }}
              allowClear
            />
            <Button
              type="primary"
              icon={<PlusOutlined />}
              loading={assignRole.isPending}
              onClick={handleAssignRole}
              disabled={!selectedRoleId}
            >
              Gán
            </Button>
          </Space>

          <Space wrap style={{ marginTop: 8 }}>
            {(group?.roleIds ?? []).length === 0 && (
              <Text type="secondary">Chưa có vai trò nào</Text>
            )}
            {(group?.roleIds ?? []).map((roleId) => (
              <Tag
                key={roleId}
                color="blue"
                closable
                onClose={() =>
                  groupId && removeRole.mutate({ groupId, roleId })
                }
              >
                {roleName(roleId)}
              </Tag>
            ))}
          </Space>
        </Space>
      ),
    },
  ];

  return (
    <Drawer
      title={group?.name ?? 'Chi tiết nhóm'}
      open={Boolean(groupId)}
      onClose={onClose}
      width={560}
      destroyOnHidden
    >
      {isLoading ? (
        <Spin style={{ display: 'block', margin: '40px auto' }} />
      ) : (
        <Tabs items={tabItems} />
      )}
    </Drawer>
  );
}
