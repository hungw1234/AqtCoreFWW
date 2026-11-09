// Notification Templates Admin Page — Table + Create/Edit modal + Delete
import { useState } from 'react';
import {
  Table,
  Button,
  Modal,
  Form,
  Input,
  Select,
  Space,
  Popconfirm,
  Tag,
  Flex,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  useNotificationTemplates,
  useCreateNotificationTemplate,
  useUpdateNotificationTemplate,
  useDeleteNotificationTemplate,
} from './notification-templates-api';
import type { NotificationTemplateDto } from './notification-templates-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

const { TextArea } = Input;

const CHANNELS = ['Email', 'SMS', 'InApp', 'Push'];

type ModalMode = 'create' | 'edit';

export function NotificationTemplatesAdminPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [modal, setModal] = useState<{ open: boolean; mode: ModalMode; record?: NotificationTemplateDto }>({
    open: false,
    mode: 'create',
  });
  const [form] = Form.useForm();

  // Fetch all data — client-side search needs full dataset
  const { data, isFetching } = useNotificationTemplates(1, 9999);
  const createMutation = useCreateNotificationTemplate();
  const updateMutation = useUpdateNotificationTemplate();
  const deleteMutation = useDeleteNotificationTemplate();

  function openCreate() {
    form.resetFields();
    setModal({ open: true, mode: 'create' });
  }

  function openEdit(record: NotificationTemplateDto) {
    form.setFieldsValue({
      templateKey: record.templateKey,
      channel: record.channel,
      subjectTemplate: record.subjectTemplate,
      bodyTemplate: record.bodyTemplate,
    });
    setModal({ open: true, mode: 'edit', record });
  }

  function closeModal() {
    setModal((s) => ({ ...s, open: false }));
    form.resetFields();
  }

  async function handleSubmit() {
    const values = await form.validateFields();
    if (modal.mode === 'create') {
      createMutation.mutate(
        { ...values },
        {
          onSuccess: () => { appMessage.success(t('page.admin.notificationTemplates.createSuccess')); closeModal(); },
          onError: () => appMessage.error(t('page.admin.notificationTemplates.saveFail')),
        },
      );
    } else if (modal.record) {
      updateMutation.mutate(
        { id: modal.record.id, subjectTemplate: values.subjectTemplate, bodyTemplate: values.bodyTemplate },
        {
          onSuccess: () => { appMessage.success(t('page.admin.notificationTemplates.updateSuccess')); closeModal(); },
          onError: () => appMessage.error(t('page.admin.notificationTemplates.saveFail')),
        },
      );
    }
  }

  function handleDelete(id: string) {
    deleteMutation.mutate(id, {
      onSuccess: () => appMessage.success(t('page.admin.notificationTemplates.deleteSuccess')),
      onError: () => appMessage.error(t('page.admin.notificationTemplates.saveFail')),
    });
  }

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

  const columns: ColumnsType<NotificationTemplateDto> = [
    { title: t('page.admin.notificationTemplates.col.key'), dataIndex: 'templateKey', key: 'templateKey', width: 180 },
    {
      title: t('page.admin.notificationTemplates.col.channel'),
      dataIndex: 'channel',
      key: 'channel',
      width: 110,
      render: (v: string) => <Tag>{v}</Tag>,
    },
    { title: t('page.admin.notificationTemplates.col.subject'), dataIndex: 'subjectTemplate', key: 'subjectTemplate', ellipsis: true },
    {
      title: t('page.admin.notificationTemplates.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 160,
      render: (v: string) => new Date(v).toLocaleString(),
    },
    {
      title: t('page.admin.notificationTemplates.col.actions'),
      key: 'actions',
      width: 100,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)} />
          <Popconfirm
            title={t('page.admin.notificationTemplates.deleteConfirm.title')}
            description={t('page.admin.notificationTemplates.deleteConfirm.description')}
            onConfirm={() => handleDelete(record.id)}
            okText={t('common.delete')}
            cancelText={t('common.cancel')}
          >
            <Button size="small" danger icon={<DeleteOutlined />} loading={deleteMutation.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const isEditing = modal.mode === 'edit';
  const saving = createMutation.isPending || updateMutation.isPending;

  return (
    <div>
      <AdminPageHeader
        title={t('page.admin.notificationTemplates.title')}
        stats={{ total: data?.items?.length ?? 0, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('page.admin.notificationTemplates.create')}
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
        <Table<NotificationTemplateDto>
          rowKey="id"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          columns={columns}
          dataSource={(data?.items ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isFetching}
          size="small"

          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
        />
      </AdminContentCard>

      <Modal
        open={modal.open}
        title={isEditing
          ? t('page.admin.notificationTemplates.editTitle')
          : t('page.admin.notificationTemplates.createTitle')}
        onCancel={closeModal}
        onOk={handleSubmit}
        okText={isEditing ? t('common.save') : t('common.create')}
        cancelText={t('common.cancel')}
        confirmLoading={saving}
        width={620}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          {/* Mirrors BE CreateNotificationTemplateCommandValidator.cs — keep in sync */}
          <Form.Item
            name="templateKey"
            label={t('page.admin.notificationTemplates.field.key')}
            validateFirst
            rules={[
              { required: true },
              // Mirrors BE CreateNotificationTemplateCommandValidator.cs — keep in sync
              { max: 100, message: t('common.validation.maxLength', { max: 100 }) },
              { pattern: /^[a-z0-9_]+$/, message: t('common.validation.pattern.lowerAlphaNum') },
            ]}
          >
            <Input disabled={isEditing} placeholder="e.g. case_submitted" />
          </Form.Item>
          <Form.Item name="channel" label={t('page.admin.notificationTemplates.field.channel')} rules={[{ required: true }]}>
            <Select disabled={isEditing} options={CHANNELS.map((c) => ({ value: c, label: c }))} />
          </Form.Item>
          <Form.Item
            name="subjectTemplate"
            label={t('page.admin.notificationTemplates.field.subject')}
            validateFirst
            rules={[
              { required: true },
              // Mirrors BE CreateNotificationTemplateCommandValidator.cs — keep in sync
              { max: 500, message: t('common.validation.maxLength', { max: 500 }) },
            ]}
          >
            <Input placeholder="Subject with {{variables}}" />
          </Form.Item>
          <Form.Item
            name="bodyTemplate"
            label={t('page.admin.notificationTemplates.field.body')}
            validateFirst
            rules={[
              { required: true },
              // Mirrors BE CreateNotificationTemplateCommandValidator.cs — keep in sync
              { max: 20000, message: t('common.validation.maxLength', { max: 20000 }) },
            ]}
          >
            <TextArea rows={6} placeholder="Body with {{variables}}" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
