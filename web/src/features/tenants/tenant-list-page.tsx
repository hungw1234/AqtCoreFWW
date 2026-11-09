import { useState } from 'react';
import { Table, Button, Space, Popconfirm } from 'antd';
import { PlusOutlined, EditOutlined, InboxOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTenants, useArchiveTenant } from './tenant-api';
import { TenantStatusTag } from './tenant-status-tag';
import { TenantFormDrawer } from './tenant-form-drawer';
import type { TenantAdminDto, TenantListParams, TenantStatus } from './tenant-types';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';

const TENANT_MANAGE = 'IDENTITY.TENANT.MANAGE';

export function TenantListPage() {
  const { hasPermission } = usePermissions();
  const canManage = hasPermission(TENANT_MANAGE);

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [editTenant, setEditTenant] = useState<TenantAdminDto | undefined>();
  const [searchInput, setSearchInput] = useState('');
  const [submittedSearch, setSubmittedSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<TenantStatus | undefined>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  const queryParams: TenantListParams = {
    search: submittedSearch || undefined,
    status: statusFilter,
    page,
    pageSize,
  };

  const { data, isFetching } = useTenants(queryParams);
  const archiveMutation = useArchiveTenant();

  const tenants = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  function openCreate() {
    setEditTenant(undefined);
    setDrawerOpen(true);
  }

  function openEdit(tenant: TenantAdminDto) {
    setEditTenant(tenant);
    setDrawerOpen(true);
  }

  async function handleArchive(id: string) {
    try {
      await archiveMutation.mutateAsync(id);
      appMessage.success('Đã lưu trữ tenant');
    } catch (err) {
      const errorMsg =
        (err as { message?: string } | undefined)?.message ?? 'Lưu trữ thất bại';
      appMessage.error(errorMsg);
    }
  }

  const columns: ColumnsType<TenantAdminDto> = [
    {
      title: 'Tên',
      dataIndex: 'name',
      key: 'name',
      sorter: (a, b) => a.name.localeCompare(b.name),
    },
    {
      title: 'Mã',
      dataIndex: 'code',
      key: 'code',
      width: 180,
      render: (code: string) => <code>{code}</code>,
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 140,
      render: (s: TenantStatus) => <TenantStatusTag status={s} />,
    },
    {
      title: 'Email liên hệ',
      dataIndex: 'contactEmail',
      key: 'contactEmail',
      ellipsis: true,
      render: (v: string | null) => v ?? '-',
    },
    {
      title: 'Người dùng',
      dataIndex: 'userCount',
      key: 'userCount',
      width: 110,
      align: 'right',
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 170,
      render: (v: string) => new Date(v).toLocaleString('vi-VN'),
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 200,
      fixed: 'right',
      render: (_, record) => (
        <Space size={4}>
          <Button
            size="small"
            icon={<EditOutlined />}
            disabled={!canManage || record.status === 'Archived'}
            onClick={() => openEdit(record)}
          >
            Sửa
          </Button>
          <Popconfirm
            title="Lưu trữ tenant này?"
            description="Tenant sẽ bị ẩn khỏi danh sách hoạt động. Có thể khôi phục bằng cách đổi trạng thái."
            okText="Lưu trữ"
            cancelText="Hủy"
            okButtonProps={{ danger: true }}
            disabled={!canManage || record.status === 'Archived'}
            onConfirm={() => void handleArchive(record.id)}
          >
            <Button
              size="small"
              danger
              icon={<InboxOutlined />}
              disabled={!canManage || record.status === 'Archived'}
            >
              Lưu trữ
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title="Quản lý tenant"
        stats={{ total: total ?? 0, label: 'tenant' }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            disabled={!canManage}
            onClick={openCreate}
          >
            Tạo tenant
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder="Tìm theo tên hoặc mã"
          searchValue={searchInput}
          onSearchChange={setSearchInput}
          onSearch={(v) => {
            setSubmittedSearch(v);
            setPage(1);
          }}
          filters={[
            {
              key: 'status',
              placeholder: 'Trạng thái',
              options: [
                { value: 'Active', label: 'Đang hoạt động' },
                { value: 'Suspended', label: 'Tạm dừng' },
                { value: 'Archived', label: 'Đã lưu trữ' },
              ],
            },
          ]}
          filterValues={{ status: statusFilter }}
          onFilterChange={(_, v) => {
            setStatusFilter(v as TenantStatus | undefined);
            setPage(1);
          }}
        />
        <Table<TenantAdminDto>
          rowKey="id"
          loading={isFetching}
          columns={columns}
          dataSource={tenants}
          size="small"
          scroll={{ x: 1100 }}
          pagination={{
            current: page,
            pageSize,
            total: total ?? undefined,
            showSizeChanger: true,
            showTotal: (t) => `Tổng ${t} tenant`,
            onChange: (p, ps) => {
              setPage(p);
              setPageSize(ps);
            },
          }}
        />
      </AdminContentCard>

      <TenantFormDrawer
        open={drawerOpen}
        mode={editTenant ? 'edit' : 'create'}
        tenant={editTenant}
        onClose={() => setDrawerOpen(false)}
      />
    </div>
  );
}
