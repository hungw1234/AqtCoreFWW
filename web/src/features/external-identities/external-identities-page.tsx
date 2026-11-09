import { useState } from 'react';
import {
  Table,
  Button,
  Popconfirm,
  Tag,
  Modal,
  Form,
  Input,
  Select,
  Flex,
} from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import {
  useExternalIdentities,
  useCreateExternalIdentity,
  useDeleteExternalIdentity,
  type ExternalIdentityDto,
  type CreateExternalIdentityDto,
} from './external-identities-api';
import { useUsers } from '@/features/users/user-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';

// Provider tag color map — distinct colors per identity protocol
const PROVIDER_COLORS: Record<ExternalIdentityDto['provider'], string> = {
  OAuth: 'blue',
  SSO:   'green',
  LDAP:  'purple',
  SAML:  'cyan',
  VNeID: 'orange',
};

const PROVIDER_OPTIONS = (['OAuth', 'SSO', 'LDAP', 'SAML', 'VNeID'] as const).map((p) => ({
  value: p,
  label: p,
}));

// ExternalIdentitiesPage — view and link external identity providers to users
export function ExternalIdentitiesPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [providerFilter, setProviderFilter] = useState<string | undefined>(undefined);
  const [form] = Form.useForm<CreateExternalIdentityDto>();

  const { data, isLoading } = useExternalIdentities();
  const { data: usersResult } = useUsers({ pageNumber: 1, pageSize: 100 });

  const userOptions = (usersResult?.items ?? []).map((u) => ({
    value: u.id,
    label: `${u.fullName} (${u.email})`,
  }));
  const createIdentity = useCreateExternalIdentity();
  const deleteIdentity = useDeleteExternalIdentity();

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  // Apply optional provider filter + search on client side
  const filteredData = items.filter((r) => {
    if (providerFilter && r.provider !== providerFilter) return false;
    if (searchText && !Object.values(r).some(v =>
      String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
    )) return false;
    return true;
  });

  const columns: ColumnsType<ExternalIdentityDto> = [
    {
      title: 'Nhà cung cấp',
      dataIndex: 'provider',
      key: 'provider',
      width: 100,
      render: (v: ExternalIdentityDto['provider']) => (
        <Tag color={PROVIDER_COLORS[v]}>{v}</Tag>
      ),
    },
    {
      title: 'Người dùng',
      dataIndex: 'userId',
      key: 'userId',
      ellipsis: true,
      width: 180,
      render: (userId: string) => {
        const user = (usersResult?.items ?? []).find((u) => u.id === userId);
        return user ? `${user.fullName}` : userId;
      },
    },
    { title: 'Tên hiển thị', dataIndex: 'displayName', key: 'displayName', ellipsis: true, width: 160, render: (v) => v ?? '—' },
    { title: 'Email', dataIndex: 'email', key: 'email', ellipsis: true, width: 180, render: (v) => v ?? '—' },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Hoạt động' : 'Vô hiệu'}</Tag>,
    },
    {
      title: 'Liên kết lúc',
      dataIndex: 'linkedAt',
      key: 'linkedAt',
      width: 160,
      render: (v: string) => new Date(v).toLocaleString('vi-VN'),
    },
    {
      title: '',
      key: 'actions',
      width: 70,
      render: (_, record) => (
        <Popconfirm
          title="Hủy liên kết danh tính này?"
          onConfirm={() => deleteIdentity.mutate(record.id, {
            onSuccess: () => appMessage.success(t('common.deleted', 'Xóa thành công')),
            onError: () => appMessage.error(t('common.error', 'Thao tác thất bại')),
          })}
          okText={t('common.confirm', 'Xác nhận')}
          cancelText={t('common.cancel', 'Hủy')}
        >
          <Button danger icon={<DeleteOutlined />} size="small" loading={deleteIdentity.isPending} />
        </Popconfirm>
      ),
    },
  ];

  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map((id) => deleteIdentity.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: `Đã xóa ${selectedIds.length} mục` }));
    }
    setSelectedIds([]);
  }

  const handleCreate = async () => {
    const values = await form.validateFields();
    try {
      await createIdentity.mutateAsync(values);
      appMessage.success(t('common.success', 'Thao tác thành công'));
      form.resetFields();
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.error', 'Thao tác thất bại'));
    }
  };

  return (
    <div>
      <AdminPageHeader
        title="Liên kết danh tính bên ngoài"
        stats={{ total, label: 'danh tính' }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); setModalOpen(true); }}>
            {t('common.add', 'Thêm')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder="Tìm theo email hoặc User ID"
          searchValue={searchText}
          onSearchChange={setSearchText}
          filters={[
            { key: 'provider', placeholder: 'Lọc theo nhà cung cấp', options: PROVIDER_OPTIONS },
          ]}
          filterValues={{ provider: providerFilter }}
          onFilterChange={(_, v) => setProviderFilter(v)}
        />
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('common.bulkDeleteConfirm', { defaultValue: `Xóa ${selectedIds.length} mục đã chọn?` })}
              onConfirm={handleBulkDelete}
            >
              <Button danger size="small">
                {t('common.deleteSelected', { defaultValue: `Xóa (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<ExternalIdentityDto>
          rowKey="id"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          columns={columns}
          dataSource={filteredData}
          loading={isLoading}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ pageSize: 20, showSizeChanger: false }}
        />
      </AdminContentCard>

      <Modal
        title="Liên kết danh tính bên ngoài"
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        confirmLoading={createIdentity.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="userId" label="Người dùng" rules={[{ required: true, message: 'Vui lòng chọn người dùng' }]}>
            <Select
              options={userOptions}
              placeholder="Tìm người dùng..."
              showSearch
              optionFilterProp="label"
              allowClear
            />
          </Form.Item>
          <Form.Item name="provider" label="Nhà cung cấp" rules={[{ required: true }]}>
            <Select options={PROVIDER_OPTIONS} placeholder="Chọn nhà cung cấp" />
          </Form.Item>
          <Form.Item name="externalId" label="ID bên ngoài" rules={[{ required: true, message: 'Vui lòng nhập External ID' }]}>
            <Input placeholder="ID trong hệ thống nhà cung cấp" />
          </Form.Item>
          <Form.Item name="displayName" label="Tên hiển thị">
            <Input />
          </Form.Item>
          <Form.Item name="email" label="Email" rules={[{ type: 'email', message: 'Email không hợp lệ' }]}>
            <Input />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
