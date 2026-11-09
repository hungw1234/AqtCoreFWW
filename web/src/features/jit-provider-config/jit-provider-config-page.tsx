import { useState } from 'react';
import {
  Table,
  Button,
  Popconfirm,
  Tag,
  Switch,
  Modal,
  Form,
  Input,
  Select,
} from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import {
  useJitProviderConfigs,
  useCreateJitProviderConfig,
  useUpdateJitProviderConfig,
  useDeleteJitProviderConfig,
  PROVIDER_TYPE_MAP,
  PROVIDER_TYPE_OPTIONS,
  type JitProviderConfigDto,
  type CreateJitProviderConfigDto,
} from './jit-provider-config-api';
import { useRoles } from '@/features/roles/roles-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';

// Provider tag colors matching external-identities-page pattern
const PROVIDER_COLORS: Record<string, string> = {
  SSO: 'green', LDAP: 'purple', VNeID: 'orange', OAuth: 'blue', SAML: 'cyan',
};

export function JitProviderConfigPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [searchText, setSearchText] = useState('');
  const [providerFilter, setProviderFilter] = useState<string | undefined>(undefined);
  const [form] = Form.useForm<CreateJitProviderConfigDto>();

  const { data: configsResult, isLoading } = useJitProviderConfigs();
  const { data: roles } = useRoles();
  const createConfig = useCreateJitProviderConfig();
  const updateConfig = useUpdateJitProviderConfig();
  const deleteConfig = useDeleteJitProviderConfig();

  const items = configsResult?.items ?? [];

  const roleOptions = (roles ?? []).map((r) => ({
    value: r.name,
    label: r.name,
  }));

  // Client-side filter by provider type + search text
  const filteredData = items.filter((r) => {
    if (providerFilter && PROVIDER_TYPE_MAP[r.providerType] !== providerFilter)
      return false;
    if (
      searchText &&
      !r.scheme.toLowerCase().includes(searchText.toLowerCase()) &&
      !r.displayName.toLowerCase().includes(searchText.toLowerCase())
    )
      return false;
    return true;
  });

  // Inline toggle JIT enabled via PUT
  function handleToggleJit(record: JitProviderConfigDto) {
    updateConfig.mutate(
      { ...record, id: record.id, jitEnabled: !record.jitEnabled },
      {
        onSuccess: () =>
          appMessage.success(
            record.jitEnabled ? 'Đã tắt JIT' : 'Đã bật JIT',
          ),
        onError: () => appMessage.error('Thao tác thất bại'),
      },
    );
  }

  const columns: ColumnsType<JitProviderConfigDto> = [
    {
      title: 'Scheme',
      dataIndex: 'scheme',
      key: 'scheme',
      width: 140,
      ellipsis: true,
    },
    {
      title: 'Tên hiển thị',
      dataIndex: 'displayName',
      key: 'displayName',
      width: 160,
      ellipsis: true,
    },
    {
      title: 'Nhà cung cấp',
      dataIndex: 'providerType',
      key: 'providerType',
      width: 110,
      render: (v: number) => {
        const label = PROVIDER_TYPE_MAP[v] ?? `#${v}`;
        return <Tag color={PROVIDER_COLORS[label] ?? 'default'}>{label}</Tag>;
      },
    },
    {
      title: 'JIT',
      dataIndex: 'jitEnabled',
      key: 'jitEnabled',
      width: 70,
      render: (v: boolean, record) => (
        <Switch
          size="small"
          checked={v}
          loading={updateConfig.isPending}
          onChange={() => handleToggleJit(record)}
        />
      ),
    },
    {
      title: 'Vai trò',
      dataIndex: 'defaultRoleName',
      key: 'defaultRoleName',
      width: 100,
    },
    {
      title: 'Duyệt',
      dataIndex: 'requireApproval',
      key: 'requireApproval',
      width: 100,
      render: (v: boolean) => (
        <Tag color={v ? 'orange' : 'green'}>
          {v ? 'Bắt buộc duyệt' : 'Tự động'}
        </Tag>
      ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 90,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>
          {v ? 'Hoạt động' : 'Vô hiệu'}
        </Tag>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: 90,
      render: (_, record) => (
        <span style={{ display: 'flex', gap: 4 }}>
          <Button
            icon={<EditOutlined />}
            size="small"
            onClick={() => openEdit(record)}
          />
          <Popconfirm
            title="Vô hiệu hóa cấu hình này?"
            onConfirm={() =>
              deleteConfig.mutate(record.id, {
                onSuccess: () => appMessage.success('Đã vô hiệu'),
                onError: () => appMessage.error('Thao tác thất bại'),
              })
            }
            okText="Xác nhận"
            cancelText="Hủy"
          >
            <Button
              danger
              icon={<DeleteOutlined />}
              size="small"
              loading={deleteConfig.isPending}
            />
          </Popconfirm>
        </span>
      ),
    },
  ];

  function openEdit(record: JitProviderConfigDto) {
    setEditingId(record.id);
    form.setFieldsValue(record);
    setModalOpen(true);
  }

  function openCreate() {
    setEditingId(null);
    form.resetFields();
    form.setFieldsValue({
      jitEnabled: false,
      requireApproval: false,
      maxProvisionsPerHour: 100,
      providerType: 1,
      defaultRoleName: 'Viewer',
    });
    setModalOpen(true);
  }

  async function handleSave() {
    const values = await form.validateFields();
    // Validate JSON fields
    if (values.claimMappingJson) {
      try {
        JSON.parse(values.claimMappingJson);
      } catch {
        appMessage.error('Claim mapping JSON không hợp lệ');
        return;
      }
    }
    if (values.allowedDomainsJson) {
      try {
        JSON.parse(values.allowedDomainsJson);
      } catch {
        appMessage.error('Allowed domains JSON không hợp lệ');
        return;
      }
    }

    try {
      if (editingId) {
        await updateConfig.mutateAsync({ ...values, id: editingId });
      } else {
        await createConfig.mutateAsync(values);
      }
      appMessage.success(t('common.success', 'Thao tác thành công'));
      setModalOpen(false);
      form.resetFields();
      setEditingId(null);
    } catch {
      appMessage.error(t('common.error', 'Thao tác thất bại'));
    }
  }

  return (
    <div>
      <AdminPageHeader
        title="Cấu hình JIT SSO"
        stats={{ total: items.length, label: 'nhà cung cấp' }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={openCreate}
          >
            {t('common.add', 'Thêm')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder="Tìm theo scheme hoặc tên"
          searchValue={searchText}
          onSearchChange={setSearchText}
          filters={[
            {
              key: 'provider',
              placeholder: 'Lọc theo nhà cung cấp',
              options: PROVIDER_TYPE_OPTIONS.map((o) => ({
                value: o.label,
                label: o.label,
              })),
            },
          ]}
          filterValues={{ provider: providerFilter }}
          onFilterChange={(_, v) => setProviderFilter(v)}
        />
        <Table<JitProviderConfigDto>
          rowKey="id"
          columns={columns}
          dataSource={filteredData}
          loading={isLoading}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ pageSize: 20, showSizeChanger: false }}
        />
      </AdminContentCard>

      <Modal
        title={editingId ? 'Chỉnh sửa cấu hình JIT' : 'Thêm cấu hình JIT'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => {
          setModalOpen(false);
          form.resetFields();
          setEditingId(null);
        }}
        confirmLoading={createConfig.isPending || updateConfig.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
        width={560}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="scheme"
            label="Tên scheme"
            rules={[{ required: true, message: 'Bắt buộc' }]}
          >
            <Input
              placeholder="vd: sso-cuca2, vneid-prod"
              disabled={!!editingId}
            />
          </Form.Item>
          <Form.Item
            name="displayName"
            label="Tên hiển thị"
            rules={[{ required: true, message: 'Bắt buộc' }]}
          >
            <Input placeholder="vd: SSO Cục A2" />
          </Form.Item>
          <Form.Item
            name="providerType"
            label="Loại nhà cung cấp"
            rules={[{ required: true, message: 'Bắt buộc' }]}
          >
            <Select options={PROVIDER_TYPE_OPTIONS} />
          </Form.Item>
          <Form.Item
            name="jitEnabled"
            label="Bật JIT Provisioning"
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
          <Form.Item
            name="defaultRoleName"
            label="Vai trò mặc định"
            rules={[{ required: true, message: 'Bắt buộc' }]}
          >
            <Select
              options={roleOptions}
              placeholder="Chọn vai trò"
              showSearch
              optionFilterProp="label"
            />
          </Form.Item>
          <Form.Item
            name="requireApproval"
            label="Yêu cầu duyệt"
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
          <Form.Item name="defaultTenantId" label="Tenant ID mặc định">
            <Input placeholder="GUID của tenant" />
          </Form.Item>
          <Form.Item name="maxProvisionsPerHour" label="Giới hạn JIT/giờ">
            <Input type="number" placeholder="100" />
          </Form.Item>
          <Form.Item
            name="allowedDomainsJson"
            label="Domain cho phép (JSON)"
          >
            <Input.TextArea
              rows={2}
              placeholder='["example.gov.vn","abc.org.vn"]'
            />
          </Form.Item>
          <Form.Item
            name="claimMappingJson"
            label="Claim mapping (JSON)"
          >
            <Input.TextArea
              rows={3}
              placeholder='{"email":"email","fullName":"name"}'
            />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
