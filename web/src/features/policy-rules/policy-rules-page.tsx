import { useState } from 'react';
import {
  Table,
  Button,
  Space,
  Popconfirm,
  Tag,
  Modal,
  Form,
  Input,
  Select,
  InputNumber,
  Switch,
  Flex,
} from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import {
  usePolicyRules,
  useCreatePolicyRule,
  useUpdatePolicyRule,
  useDeletePolicyRule,
  type PolicyRuleDto,
  type CreatePolicyRuleDto,
  type UpdatePolicyRuleDto,
} from './policy-rules-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

// PolicyRulesPage — manage attribute-based access policy rules with permission codes and conditions
export function PolicyRulesPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [editingRule, setEditingRule] = useState<PolicyRuleDto | null>(null);
  const [form] = Form.useForm<CreatePolicyRuleDto | UpdatePolicyRuleDto>();

  const { data, isLoading } = usePolicyRules();
  const createRule = useCreatePolicyRule();
  const updateRule = useUpdatePolicyRule();
  const deleteRule = useDeletePolicyRule();

  const rules = data ?? [];
  const total = rules.length;

  const columns: ColumnsType<PolicyRuleDto> = [
    { title: 'Mã quy tắc', dataIndex: 'code', key: 'code', width: 160, ellipsis: true },
    {
      title: 'Mã quyền',
      dataIndex: 'permissionCode',
      key: 'permissionCode',
      width: 160,
      ellipsis: true,
      render: (v: string) => <Tag>{v}</Tag>,
    },
    {
      title: 'Hiệu lực',
      dataIndex: 'effect',
      key: 'effect',
      width: 100,
      render: (v: string) => <Tag color={v === 'Allow' ? 'green' : 'red'}>{v}</Tag>,
    },
    { title: 'Ưu tiên', dataIndex: 'priority', key: 'priority', width: 90, sorter: (a, b) => a.priority - b.priority },
    {
      title: 'Ghi log',
      dataIndex: 'logOnDeny',
      key: 'logOnDeny',
      width: 90,
      render: (v: boolean) => v ? <Tag color="orange">Có</Tag> : null,
    },
    {
      title: 'Kích hoạt',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) =>
        v ? <Tag color="green">Bật</Tag> : <Tag color="default">Tắt</Tag>,
    },
    {
      title: 'Điều kiện',
      dataIndex: 'conditionExpression',
      key: 'conditionExpression',
      ellipsis: true,
      render: (v?: string) => v
        ? <code style={{ fontSize: 12, color: '#555' }}>{v}</code>
        : <span style={{ color: '#bbb' }}>—</span>,
    },
    {
      title: '',
      key: 'actions',
      width: 80,
      render: (_, record) => (
        <Space size="small">
          <Button
            size="small"
            icon={<EditOutlined />}
            onClick={() => { setEditingRule(record); form.setFieldsValue(record); setModalOpen(true); }}
          />
          <Popconfirm
            title="Xóa quy tắc này?"
            onConfirm={() => deleteRule.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.deleteSuccess', { defaultValue: 'Xóa thành công' })),
              onError: () => appMessage.error(t('common.deleteFailed', { defaultValue: 'Xóa thất bại' })),
            })}
            okText={t('common.confirm', 'Xác nhận')}
            cancelText={t('common.cancel', 'Hủy')}
          >
            <Button danger icon={<DeleteOutlined />} size="small" loading={deleteRule.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map((id) => deleteRule.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: `Đã xóa ${selectedIds.length} mục` }));
    }
    setSelectedIds([]);
  }

  const handleSave = async () => {
    const values = await form.validateFields();
    try {
      if (editingRule) {
        await updateRule.mutateAsync({ ...(values as UpdatePolicyRuleDto), id: editingRule.id });
      } else {
        await createRule.mutateAsync(values as CreatePolicyRuleDto);
      }
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
      form.resetFields();
      setEditingRule(null);
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
    }
  };

  return (
    <div>
      <AdminPageHeader
        title="Quy tắc chính sách truy cập"
        stats={{ total, label: 'quy tắc' }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => { setEditingRule(null); form.resetFields(); setModalOpen(true); }}
          >
            {t('common.add', 'Thêm')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
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
        <Table<PolicyRuleDto>
          rowKey="id"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          columns={columns}
          dataSource={rules.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
        />
      </AdminContentCard>

      <Modal
        title={editingRule ? 'Sửa quy tắc chính sách' : 'Tạo quy tắc chính sách'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => { setModalOpen(false); setEditingRule(null); form.resetFields(); }}
        confirmLoading={createRule.isPending || updateRule.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
        width={560}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="code" label="Mã quy tắc" rules={[{ required: true, message: 'Vui lòng nhập mã quy tắc' }]}>
            <Input placeholder="VD: DENY_CROSS_DEPT_APPROVE" />
          </Form.Item>
          <Form.Item name="permissionCode" label="Mã quyền" rules={[{ required: true, message: 'Vui lòng nhập mã quyền' }]}>
            <Input placeholder="VD: cases.approve" />
          </Form.Item>
          <Form.Item name="conditionExpression" label="Biểu thức điều kiện (tuỳ chọn)">
            <Input.TextArea rows={4} placeholder="VD: user.department == resource.department" style={{ fontFamily: 'monospace', fontSize: 13 }} />
          </Form.Item>
          <Form.Item name="effect" label="Hiệu lực" rules={[{ required: true, message: 'Vui lòng chọn hiệu lực' }]} initialValue="Allow">
            <Select options={[{ value: 'Allow', label: 'Allow' }, { value: 'Deny', label: 'Deny' }]} />
          </Form.Item>
          <Form.Item name="priority" label="Ưu tiên (số nhỏ hơn = cao hơn)" rules={[{ required: true, message: 'Vui lòng nhập mức ưu tiên' }]} initialValue={100}>
            <InputNumber min={1} max={9999} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="logOnDeny" label="Ghi log khi từ chối" valuePropName="checked" initialValue={false}>
            <Switch />
          </Form.Item>
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={2} />
          </Form.Item>
          {editingRule && (
            <Form.Item name="isActive" label="Kích hoạt" valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
        </Form>
      </Modal>
    </div>
  );
}
