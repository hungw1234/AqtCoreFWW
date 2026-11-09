import { useState } from 'react';
import {
  Table,
  Button,
  Space,
  Tag,
  Modal,
  Form,
  Input,
  Popconfirm,
  Drawer,
  Descriptions,
  Flex,
} from 'antd';
import { PlusOutlined, EyeOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';
import {
  usePartners, usePartner, useCreatePartner, useUpdatePartner, useDeletePartner,
  useSuspendPartner, useActivatePartner,
  type PartnerDto, type PartnerStatus, type CreatePartnerDto,
} from './integration-api';
import { appMessage } from '@/core/api/notification-bridge';

const STATUS_COLOR: Record<PartnerStatus, string> = {
  Active: 'green',
  Suspended: 'orange',
  Deactivated: 'default',
};

// ─── Detail drawer ────────────────────────────────────────────────────────────

function PartnerDetailDrawer({ partnerId, open, onClose }: {
  partnerId: string; open: boolean; onClose: () => void;
}) {
  const { t } = useTranslation();
  const { data, isLoading } = usePartner(partnerId);
  const suspendMutation = useSuspendPartner();
  const activateMutation = useActivatePartner();

  return (
    <Drawer title={data?.name ?? t('integration.partners.detailTitle')} open={open} onClose={onClose} width={520}>
      {!isLoading && data && (
        <Space direction="vertical" style={{ width: '100%' }} size={16}>
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label={t('integration.partners.col.code')}>{data.code}</Descriptions.Item>
            <Descriptions.Item label={t('integration.partners.col.email')}>{data.contactEmail ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('integration.partners.col.phone')}>{data.contactPhone ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('integration.partners.col.endpoint')}>{data.endpoint ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('integration.partners.col.authScheme')}>{data.authScheme ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('integration.partners.col.status')}>
              <Tag color={STATUS_COLOR[data.status]}>{data.status}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label={t('integration.partners.col.createdAt')}>
              {dayjs(data.createdAt).format('DD/MM/YYYY HH:mm')}
            </Descriptions.Item>
          </Descriptions>
          <Space>
            {data.status === 'Active' && (
              <Popconfirm title={t('integration.partners.confirmSuspend')}
                onConfirm={() => suspendMutation.mutate(data.id, {
                  onSuccess: () => appMessage.success(t('integration.partners.suspendSuccess', 'Thao tác thành công')),
                  onError: () => appMessage.error(t('integration.partners.suspendError', 'Thao tác thất bại')),
                })}
                okText={t('common.confirm')} cancelText={t('common.cancel')}>
                <Button danger loading={suspendMutation.isPending}>{t('integration.partners.suspend')}</Button>
              </Popconfirm>
            )}
            {data.status === 'Suspended' && (
              <Button type="primary" loading={activateMutation.isPending}
                onClick={() => activateMutation.mutate(data.id, {
                  onSuccess: () => appMessage.success(t('integration.partners.activateSuccess', 'Thao tác thành công')),
                  onError: () => appMessage.error(t('integration.partners.activateError', 'Thao tác thất bại')),
                })}>
                {t('integration.partners.activate')}
              </Button>
            )}
          </Space>
        </Space>
      )}
    </Drawer>
  );
}

// ─── Create / Edit modal ──────────────────────────────────────────────────────

function PartnerFormModal({ open, onClose, editPartner }: {
  open: boolean; onClose: () => void; editPartner?: PartnerDto;
}) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreatePartnerDto>();
  const createMutation = useCreatePartner();
  const updateMutation = useUpdatePartner();
  const isEdit = Boolean(editPartner);

  async function handleSave() {
    const values = await form.validateFields();
    try {
      if (isEdit && editPartner) {
        await updateMutation.mutateAsync({ id: editPartner.id, ...values });
      } else {
        await createMutation.mutateAsync(values);
      }
      appMessage.success(t('common.success'));
      form.resetFields();
      onClose();
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  const isPending = createMutation.isPending || updateMutation.isPending;

  return (
    <Modal
      title={isEdit ? t('integration.partners.editTitle') : t('integration.partners.createTitle')}
      open={open}
      onOk={handleSave}
      onCancel={() => { form.resetFields(); onClose(); }}
      confirmLoading={isPending}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      destroyOnHidden
      afterOpenChange={(visible) => { if (visible && editPartner) form.setFieldsValue(editPartner); }}
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        <Form.Item
          name="name"
          label={t('integration.partners.col.name')}
          validateFirst
          rules={[
            { required: true },
            // Mirrors BE CreatePartnerCommandValidator.cs — keep in sync
            { max: 200, message: t('common.validation.maxLength', { max: 200 }) },
          ]}
        >
          <Input />
        </Form.Item>
        <Form.Item
          name="code"
          label={t('integration.partners.col.code')}
          validateFirst
          rules={[
            { required: true },
            // Mirrors BE CreatePartnerCommandValidator.cs — keep in sync
            { max: 50, message: t('common.validation.maxLength', { max: 50 }) },
          ]}
        >
          <Input />
        </Form.Item>
        <Form.Item name="contactEmail" label={t('integration.partners.col.email')}
          rules={[{ type: 'email' }]}>
          <Input />
        </Form.Item>
        <Form.Item name="contactPhone" label={t('integration.partners.col.phone')}>
          <Input />
        </Form.Item>
        <Form.Item name="endpoint" label={t('integration.partners.col.endpoint')}>
          <Input placeholder="https://partner.example.com/api" />
        </Form.Item>
        <Form.Item name="authScheme" label={t('integration.partners.col.authScheme')}>
          <Input placeholder="Bearer, ApiKey, Basic..." />
        </Form.Item>
      </Form>
    </Modal>
  );
}

// ─── List page ────────────────────────────────────────────────────────────────

// PartnerListPage — integration partner management with status badges and CRUD actions
export function PartnerListPage() {
  const { t } = useTranslation();
  const [createOpen, setCreateOpen] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [editPartner, setEditPartner] = useState<PartnerDto | undefined>();
  const [detailId, setDetailId] = useState<string | null>(null);

  // Debounce search input by 300ms — prevents API call on every keystroke
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { antPagination, toQueryParams } = useServerPagination(20, [debouncedSearch]);

  const { data, isFetching } = usePartners({
    ...toQueryParams(),
    search: debouncedSearch || undefined,
  });
  const deleteMutation = useDeletePartner();

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map((id) => deleteMutation.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: `Đã xóa ${selectedIds.length} mục` }));
    }
    setSelectedIds([]);
  }

  const columns: ColumnsType<PartnerDto> = [
    { title: t('integration.partners.col.name'), dataIndex: 'name', key: 'name', ellipsis: true },
    { title: t('integration.partners.col.code'), dataIndex: 'code', key: 'code', width: 120 },
    { title: t('integration.partners.col.email'), dataIndex: 'contactEmail', key: 'contactEmail', ellipsis: true, width: 200 },
    {
      title: t('integration.partners.col.status'), dataIndex: 'status', key: 'status', width: 110,
      render: (v: PartnerStatus) => <Tag color={STATUS_COLOR[v]}>{v}</Tag>,
    },
    {
      title: t('integration.partners.col.createdAt'), dataIndex: 'createdAt', key: 'createdAt', width: 130,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: '', key: 'actions', width: 130,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" icon={<EyeOutlined />} onClick={() => setDetailId(record.id)} />
          <Button size="small" icon={<EditOutlined />} onClick={() => setEditPartner(record)} />
          <Popconfirm title={t('integration.partners.confirmDelete')}
            onConfirm={() => deleteMutation.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.deleted', 'Xóa thành công')),
              onError: () => appMessage.error(t('common.error', 'Thao tác thất bại')),
            })}
            okText={t('common.confirm')} cancelText={t('common.cancel')}>
            <Button size="small" icon={<DeleteOutlined />} danger loading={deleteMutation.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('integration.partners.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
            {t('common.add')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchInput}
          onSearchChange={setSearchInput}
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
        <Table<PartnerDto>
          rowKey="id"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          columns={columns}
          dataSource={items}
          loading={isFetching}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ ...antPagination, total }}
        />
      </AdminContentCard>

      <PartnerFormModal open={createOpen} onClose={() => setCreateOpen(false)} />

      {editPartner && (
        <PartnerFormModal
          open={Boolean(editPartner)}
          onClose={() => setEditPartner(undefined)}
          editPartner={editPartner}
        />
      )}

      {detailId && (
        <PartnerDetailDrawer
          partnerId={detailId}
          open={Boolean(detailId)}
          onClose={() => setDetailId(null)}
        />
      )}
    </div>
  );
}
