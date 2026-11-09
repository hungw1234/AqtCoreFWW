import { useState } from 'react';
import {
  Table,
  Button,
  Select,
  Popconfirm,
  Tag,
  Modal,
  Form,
  Input,
  InputNumber,
  Flex,
} from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import {
  useDataScopeTypes,
  useRoleScopes,
  useCreateRoleScope,
  useDeleteRoleScope,
  type RoleScopeDto,
  type CreateRoleScopeDto,
} from './data-scopes-api';
import { useRoles } from '@/features/roles/roles-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

// DataScopesPage — role selector on left; scopes table for selected role on right
export function DataScopesPage() {
  const { t } = useTranslation();
  const [selectedRole, setSelectedRole] = useState<string | null>(null);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm<CreateRoleScopeDto>();

  const { data: roles = [], isLoading: loadingRoles } = useRoles();
  const { data: scopeTypes = [] } = useDataScopeTypes();
  const { data: scopes = [], isLoading: loadingScopes } = useRoleScopes(selectedRole);
  const createScope = useCreateRoleScope(selectedRole);
  const deleteScope = useDeleteRoleScope(selectedRole);

  // Build Select options from BE scope type list
  const scopeTypeOptions = scopeTypes.map((s) => ({
    value: s.type,
    label: `${s.type} — ${s.description}`,
  }));

  const columns: ColumnsType<RoleScopeDto> = [
    {
      title: 'Loại phạm vi',
      dataIndex: 'dataScopeTypeId',
      key: 'dataScopeTypeId',
      render: (v: string) => <Tag color="blue">{v}</Tag>,
    },
    {
      title: 'Trường phạm vi',
      dataIndex: 'scopeField',
      key: 'scopeField',
      width: 160,
      render: (v?: string) => v ?? '—',
    },
    {
      title: 'Giá trị',
      dataIndex: 'scopeValue',
      key: 'scopeValue',
      width: 160,
      render: (v?: string) => v ?? '—',
    },
    {
      title: 'Ưu tiên',
      dataIndex: 'priority',
      key: 'priority',
      width: 90,
      render: (v?: number) => v ?? '—',
    },
    {
      title: '',
      key: 'actions',
      width: 70,
      render: (_, record) => (
        <Popconfirm
          title="Xóa phạm vi này?"
          onConfirm={() => deleteScope.mutate(record.id, {
            onSuccess: () => appMessage.success(t('common.deleted', 'Xóa thành công')),
            onError: () => appMessage.error(t('common.error', 'Thao tác thất bại')),
          })}
          okText={t('common.confirm', 'Xác nhận')}
          cancelText={t('common.cancel', 'Hủy')}
          disabled={!selectedRole}
        >
          <Button danger size="small" icon={<DeleteOutlined />} loading={deleteScope.isPending} />
        </Popconfirm>
      ),
    },
  ];

  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map((id) => deleteScope.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: `Đã xóa ${selectedIds.length} mục` }));
    }
    setSelectedIds([]);
  }

  const handleAdd = async () => {
    const values = await form.validateFields();
    try {
      await createScope.mutateAsync(values);
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
        title="Phạm vi dữ liệu theo vai trò"
        stats={{ total: scopes.length, label: 'phạm vi' }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            disabled={!selectedRole}
            onClick={() => { form.resetFields(); setModalOpen(true); }}
          >
            Thêm phạm vi
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        {/* Role selector toolbar */}
        <div style={{ padding: '16px 24px', borderBottom: '1px solid var(--gov-border)' }}>
          <Select
            placeholder="Chọn vai trò"
            style={{ width: 300 }}
            loading={loadingRoles}
            allowClear
            showSearch
            options={roles.map((r) => ({ value: r.id, label: r.name }))}
            value={selectedRole ?? undefined}
            onChange={(v) => setSelectedRole(v ?? null)}
            onClear={() => setSelectedRole(null)}
          />
        </div>

        {/* Bulk action bar */}
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
        {/* Scopes table for selected role */}
        <Table<RoleScopeDto>
          rowKey="id"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          columns={columns}
          dataSource={scopes.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={loadingScopes}
          size="small"
          locale={{ emptyText: selectedRole ? 'Chưa có phạm vi nào' : 'Chọn vai trò để xem phạm vi' }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
        />
      </AdminContentCard>

      <Modal
        title="Thêm phạm vi dữ liệu"
        open={modalOpen}
        onOk={handleAdd}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        confirmLoading={createScope.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="dataScopeTypeId"
            label="Loại phạm vi"
            rules={[{ required: true, message: 'Vui lòng chọn loại phạm vi' }]}
          >
            <Select
              placeholder="Chọn loại phạm vi"
              options={scopeTypeOptions.length > 0 ? scopeTypeOptions : undefined}
              showSearch
              allowClear
            />
          </Form.Item>
          <Form.Item name="scopeField" label="Trường phạm vi (tuỳ chọn)">
            <Input placeholder="VD: department, region" />
          </Form.Item>
          <Form.Item name="scopeValue" label="Giá trị phạm vi (tuỳ chọn)">
            <Input placeholder="VD: HCM, HANOI" />
          </Form.Item>
          <Form.Item name="priority" label="Ưu tiên (tuỳ chọn)">
            <InputNumber min={1} max={9999} style={{ width: '100%' }} placeholder="Số nhỏ hơn = ưu tiên cao hơn" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
