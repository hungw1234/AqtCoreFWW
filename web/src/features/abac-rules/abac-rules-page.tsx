import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tag, Modal, Form, Input, Select, Flex } from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useAbacRules,
  useCreateAbacRule,
  useUpdateAbacRule,
  useDeleteAbacRule,
  type AbacRuleDto,
  type CreateAbacRuleDto,
} from './abac-rules-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';

// ABAC Rules admin page — list, create, and delete attribute-based access control rules
export function AbacRulesPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [editingRule, setEditingRule] = useState<AbacRuleDto | null>(null);
  const [form] = Form.useForm<CreateAbacRuleDto>();

  const { data, isLoading } = useAbacRules();
  const createRule = useCreateAbacRule();
  const updateRule = useUpdateAbacRule();
  const deleteRule = useDeleteAbacRule();

  const rules = data ?? [];
  const total = rules.length;

  const columns: ColumnsType<AbacRuleDto> = [
    { title: t('abacRules.col.resource'), dataIndex: 'resource', key: 'resource', width: 160, ellipsis: true },
    { title: t('abacRules.col.action'), dataIndex: 'action', key: 'action', width: 140, ellipsis: true },
    { title: t('abacRules.col.attributeKey'), dataIndex: 'attributeKey', key: 'attributeKey', width: 170, ellipsis: true },
    // no width — flex fills remaining space so long attribute values are not truncated
    { title: t('abacRules.col.attributeValue'), dataIndex: 'attributeValue', key: 'attributeValue', ellipsis: true },
    {
      title: t('abacRules.col.effect'),
      dataIndex: 'effect',
      key: 'effect',
      width: 100,
      render: (v: string) => <Tag color={v === 'Allow' ? 'green' : 'red'}>{v}</Tag>,
    },
    { title: t('abacRules.col.tenantId'), dataIndex: 'tenantId', key: 'tenantId', width: 150, ellipsis: true, render: (v) => v ?? '—' },
    {
      title: '',
      key: 'actions',
      width: 80,
      render: (_, record) => (
        <Space size="small">
          <Button
            size="small"
            icon={<EditOutlined />}
            onClick={() => { setEditingRule(record); form.setFieldsValue({ resource: record.resource, action: record.action, attributeKey: record.attributeKey, attributeValue: record.attributeValue, effect: record.effect }); setModalOpen(true); }}
          />
          <Popconfirm
            title={t('abacRules.deleteConfirm')}
            onConfirm={() => deleteRule.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.deleteSuccess', { defaultValue: 'Xóa thành công' })),
              onError: () => appMessage.error(t('common.deleteFailed', { defaultValue: 'Xóa thất bại' })),
            })}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
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
        await updateRule.mutateAsync({ ...values, id: editingRule.id });
      } else {
        await createRule.mutateAsync(values);
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
        title={t('abacRules.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { setEditingRule(null); form.resetFields(); setModalOpen(true); }}>
            {t('common.add')}
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
        <Table<AbacRuleDto>
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
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
        />
      </AdminContentCard>

      <Modal
        title={editingRule ? t('abacRules.editTitle', { defaultValue: 'Sửa quy tắc ABAC' }) : t('abacRules.createTitle')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => { setModalOpen(false); setEditingRule(null); form.resetFields(); }}
        confirmLoading={createRule.isPending || updateRule.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="resource" label={t('abacRules.col.resource')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="action" label={t('abacRules.col.action')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="attributeKey" label={t('abacRules.col.attributeKey')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="attributeValue" label={t('abacRules.col.attributeValue')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="effect" label={t('abacRules.col.effect')} rules={[{ required: true }]} initialValue="Allow">
            <Select options={[{ value: 'Allow', label: 'Allow' }, { value: 'Deny', label: 'Deny' }]} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
