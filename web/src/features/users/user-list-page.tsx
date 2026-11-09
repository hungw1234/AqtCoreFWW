import { useState } from 'react';
import { Table, Tag, Button, Space, Popconfirm, Tooltip, Flex } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useUsers, useDeleteUser, useSyncRoles } from './user-api';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { appMessage } from '@/core/api/notification-bridge';
// useDebouncedValue removed — search is now submit-based (Enter/button)
import { UserFormModal } from './user-form-modal';
import { RoleSyncSelect } from './role-sync-select';
import type { UserDto, UserListParams } from './user-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { AdminContentCard } from '@/shared/components/admin-content-card';

// UserListPage — paginated admin user table with create/edit/delete
export function UserListPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editUser, setEditUser] = useState<UserDto | null>(null);
  const [searchInput, setSearchInput] = useState<string>('');
  const [submittedSearch, setSubmittedSearch] = useState<string>('');
  const [filters, setFilters] = useState<Pick<UserListParams, 'role' | 'status'>>({});
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const { antPagination, toQueryParams } = useServerPagination(20, [submittedSearch, filters]);

  const queryParams: UserListParams = {
    ...toQueryParams(),
    ...filters,
    search: submittedSearch || undefined,
  };

  const { data, isFetching } = useUsers(queryParams);
  const deleteMutation = useDeleteUser();
  const syncRolesMutation = useSyncRoles();

  // Available roles for assignment
  const ASSIGNABLE_ROLES = ['Admin', 'SystemAdmin', 'GovOfficer', 'Citizen'];

  const users = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  function openCreate() {
    setEditUser(null);
    setModalOpen(true);
  }

  // Bulk delete — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map(id => deleteMutation.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: 'Xóa thành công' }));
    }
    setSelectedIds([]);
  }

  function openEdit(user: UserDto) {
    setEditUser(user);
    setModalOpen(true);
  }

  const COLUMNS: ColumnsType<UserDto> = [
    {
      title: t('page.admin.users.col.fullName'),
      dataIndex: 'fullName',
      key: 'fullName',
      width: 180,
      ellipsis: true,
      sorter: true,
    },
    {
      title: t('page.admin.users.col.email'),
      dataIndex: 'email',
      key: 'email',
      width: 200,
      ellipsis: true,
    },
    {
      title: t('page.admin.users.col.roles'),
      dataIndex: 'roles',
      key: 'roles',
      render: (roles: string[], record) => (
        <RoleSyncSelect
          userId={record.id}
          currentRoles={roles as string[]}
          assignableRoles={ASSIGNABLE_ROLES}
          syncRolesMutation={syncRolesMutation}
          onSuccess={() => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }))}
          onError={() => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }))}
        />
      ),
    },
    {
      title: t('page.admin.users.col.status'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 110,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'red'}>
          {v ? t('page.admin.users.status.active', 'Hoạt động') : t('page.admin.users.status.inactive', 'Vô hiệu')}
        </Tag>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: 80,
      fixed: 'right' as const,
      render: (_, record) => (
        <Space size={4}>
          <Tooltip title={t('common.edit')}>
            <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)} />
          </Tooltip>
          <Popconfirm
            title={t('page.admin.users.deleteConfirm.title')}
            description={t('page.admin.users.deleteConfirm.description')}
            okText={t('common.delete')}
            cancelText={t('common.cancel')}
            okButtonProps={{ danger: true }}
            onConfirm={() => deleteMutation.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.deleteSuccess', { defaultValue: 'Xóa thành công' })),
              onError: () => appMessage.error(t('common.deleteFailed', { defaultValue: 'Xóa thất bại' })),
            })}
          >
            <Tooltip title={t('common.delete')}>
              <Button size="small" danger icon={<DeleteOutlined />} />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const ROLE_OPTIONS = ASSIGNABLE_ROLES.map((r) => ({ label: r, value: r }));
  const STATUS_OPTIONS = [
    { label: t('page.admin.users.status.active', 'Hoạt động'), value: 'true' },
    { label: t('page.admin.users.status.inactive', 'Vô hiệu'), value: 'false' },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('page.admin.users.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('page.admin.users.create')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('page.admin.users.searchPlaceholder')}
          searchValue={searchInput}
          onSearchChange={setSearchInput}
          onSearch={setSubmittedSearch}
          filters={[
            { key: 'role', placeholder: t('page.admin.users.filterRole'), options: ROLE_OPTIONS },
            { key: 'status', placeholder: t('page.admin.users.filterStatus'), options: STATUS_OPTIONS },
          ]}
          filterValues={filters}
          onFilterChange={(k, v) => setFilters((f) => ({ ...f, [k]: v }))}
        />
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('common.bulkDeleteConfirm', { defaultValue: `Xóa ${selectedIds.length} mục đã chọn?` })}
              onConfirm={handleBulkDelete}
              okText={t('common.yes', { defaultValue: 'Có' })}
              cancelText={t('common.no', { defaultValue: 'Không' })}
            >
              <Button danger size="small">
                {t('common.deleteSelected', { defaultValue: `Xóa (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<UserDto>
          rowKey="id"
          columns={COLUMNS}
          dataSource={users}
          loading={isFetching}
          size="small"
          scroll={{ x: 900 }}
          pagination={{ ...antPagination, total }}
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
        />
      </AdminContentCard>

      <UserFormModal
        open={modalOpen}
        editUser={editUser}
        onClose={() => setModalOpen(false)}
      />
    </div>
  );
}
