import { useState, useMemo } from 'react';
import { Table, Button, Space, Popconfirm, Tag, Modal, Form, Input } from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined, CheckCircleOutlined, StopOutlined, EyeOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useRuleSets,
  useCreateRuleSet,
  useUpdateRuleSet,
  useActivateRuleSet,
  useDeprecateRuleSet,
  useDeleteRuleSet,
  type RuleSetDto,
  type RuleSetStatus,
  type CreateRuleSetDto,
} from './rules-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';

// Status color map for rule set badges
const STATUS_COLOR: Record<RuleSetStatus, string> = {
  Draft: 'default',
  Active: 'green',
  Deprecated: 'red',
};

// RuleSetListPage — CRUD table with version badges and lifecycle actions
export function RuleSetListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [editing, setEditing] = useState<RuleSetDto | null>(null);
  const [form] = Form.useForm<CreateRuleSetDto>();

  // Fetch full set for client-side search/filter. BE clamps to PickerMax=1000 (ARCH-FU2.6);
  // RuleSet is a config entity (tens typical) — 1000-cap = 10x headroom. If rule-set count
  // approaches 1000, refactor to server-side search + pagination instead of bumping the cap.
  const { data, isFetching } = useRuleSets({ pageNumber: 1, pageSize: 1000 });
  const createMutation = useCreateRuleSet();
  const updateMutation = useUpdateRuleSet();
  const activateMutation = useActivateRuleSet();
  const deprecateMutation = useDeprecateRuleSet();
  const deleteMutation = useDeleteRuleSet();

  const items = data?.items ?? [];

  const columns: ColumnsType<RuleSetDto> = useMemo(() => [
    { title: t('rules.col.name'), dataIndex: 'name', key: 'name', ellipsis: true },
    {
      title: t('rules.col.version'),
      dataIndex: 'version',
      key: 'version',
      width: 90,
      render: (v: number) => <Tag color="blue">v{v}</Tag>,
    },
    {
      title: t('rules.col.status'),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: RuleSetStatus) => <Tag color={STATUS_COLOR[v]}>{v}</Tag>,
    },
    {
      title: t('rules.col.description'),
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
      render: (v?: string) => v ?? '—',
    },
    {
      title: t('rules.col.updatedAt'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 130,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY') : '—'),
    },
    {
      title: '',
      key: 'actions',
      width: 180,
      render: (_, record) => (
        <Space size="small">
          <Button
            size="small"
            icon={<EyeOutlined />}
            onClick={() => navigate({ to: '/admin/rules/$ruleSetId', params: { ruleSetId: record.id } })}
          />
          <Button
            size="small"
            icon={<EditOutlined />}
            onClick={() => { setEditing(record); form.setFieldsValue(record); setModalOpen(true); }}
          />
          {record.status === 'Draft' && (
            <Popconfirm title={t('rules.activateConfirm')} onConfirm={() => activateMutation.mutate(record.id, {
              onSuccess: () => appMessage.success(t('rules.activateSuccess', 'Kích hoạt thành công')),
              onError: () => appMessage.error(t('rules.activateError', 'Kích hoạt thất bại')),
            })} okText={t('common.confirm')} cancelText={t('common.cancel')}>
              <Button size="small" icon={<CheckCircleOutlined />} type="primary" />
            </Popconfirm>
          )}
          {record.status === 'Active' && (
            <Popconfirm title={t('rules.deprecateConfirm')} onConfirm={() => deprecateMutation.mutate(record.id, {
              onSuccess: () => appMessage.success(t('rules.deprecateSuccess', 'Thao tác thành công')),
              onError: () => appMessage.error(t('rules.deprecateError', 'Thao tác thất bại')),
            })} okText={t('common.confirm')} cancelText={t('common.cancel')}>
              <Button size="small" icon={<StopOutlined />} danger />
            </Popconfirm>
          )}
          <Popconfirm title={t('rules.deleteConfirm')} onConfirm={() => deleteMutation.mutate(record.id, {
            onSuccess: () => appMessage.success(t('common.deleted', 'Xóa thành công')),
            onError: () => appMessage.error(t('common.error', 'Thao tác thất bại')),
          })} okText={t('common.confirm')} cancelText={t('common.cancel')}>
            <Button size="small" icon={<DeleteOutlined />} danger loading={deleteMutation.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ], [t, navigate, activateMutation, deprecateMutation, deleteMutation, form]);

  async function handleSave() {
    const values = await form.validateFields();
    try {
      if (editing) {
        await updateMutation.mutateAsync({ id: editing.id, body: values });
      } else {
        await createMutation.mutateAsync(values);
      }
      appMessage.success(t('common.success'));
      form.resetFields();
      setEditing(null);
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  return (
    <div>
      <AdminPageHeader
        title={t('rules.title')}
        stats={{ total: items.length, label: t('common.items') }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => { setEditing(null); form.resetFields(); setModalOpen(true); }}
          >
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
        <Table<RuleSetDto>
          rowKey="id"
          columns={columns}
          dataSource={items.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isFetching}
          size="small"
          scroll={{ x: 800 }}

          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
        />
      </AdminContentCard>

      <Modal
        title={editing ? t('rules.editTitle') : t('rules.createTitle')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => { setModalOpen(false); setEditing(null); form.resetFields(); }}
        confirmLoading={createMutation.isPending || updateMutation.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="name" label={t('rules.col.name')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('rules.col.description')}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
