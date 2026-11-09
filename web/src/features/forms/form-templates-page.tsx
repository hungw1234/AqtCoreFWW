import { useState, useCallback } from 'react';
import { Table, Tag, Button, Space, Modal, Form, Input, Popconfirm, Flex, Tooltip, Alert, List, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, DeleteOutlined, EditOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useFormTemplates, useCreateTemplate, useDeleteTemplate } from './form-api';
import { useBpReferencesForFormTemplate } from '@/features/business-process/business-process-admin-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import type { FormTemplateDto, FormStatus } from './form-types';
import { appMessage } from '@/core/api/notification-bridge';

const STATUS_COLOR: Record<FormStatus, string> = {
  Draft: 'default',
  Materializing: 'blue',
  Active: 'green',
  Inactive: 'orange',
};

// BpRefBlockModal — shown when a form template is referenced by BPs, blocking delete
function BpRefBlockModal({
  open,
  templateId,
  onClose,
}: {
  open: boolean;
  templateId: string | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { data: refs = [], isLoading } = useBpReferencesForFormTemplate(open ? templateId : null);

  const roleLabel = (role: string) => {
    if (role === 'intake') return t('businessProcessAdmin.refs.role.intake', 'Biểu mẫu đầu vào');
    if (role === 'state-form') return t('businessProcessAdmin.refs.role.stateForm', 'Biểu mẫu trạng thái');
    if (role === 'transition-form') return t('businessProcessAdmin.refs.role.transitionForm', 'Biểu mẫu chuyển trạng thái');
    return role;
  };

  return (
    <Modal
      open={open}
      title={
        <Space>
          <ExclamationCircleOutlined style={{ color: '#faad14' }} />
          {t('businessProcessAdmin.refs.title', 'Không thể xóa mẫu biểu')}
        </Space>
      }
      onCancel={onClose}
      footer={
        <Button onClick={onClose}>{t('common.cancel', 'Đóng')}</Button>
      }
      destroyOnHidden
    >
      <Alert
        type="warning"
        message={t('businessProcessAdmin.refs.cannotDelete', 'Mẫu biểu này đang được sử dụng bởi các quy trình sau. Hãy xóa các tham chiếu trước khi xóa mẫu biểu.')}
        style={{ marginBottom: 12 }}
      />
      <List
        loading={isLoading}
        size="small"
        dataSource={refs}
        renderItem={(ref) => (
          <List.Item>
            <Space>
              <Typography.Text code>{ref.code}</Typography.Text>
              <Typography.Text>{ref.name}</Typography.Text>
              <Tag color="blue">{roleLabel(ref.role)}</Tag>
              {!ref.isActive && <Tag color="default">Inactive</Tag>}
            </Space>
          </List.Item>
        )}
      />
    </Modal>
  );
}

// FormTemplatesPage — list all form templates; supports create and delete (Draft only)
export function FormTemplatesPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [bpRefCheckId, setBpRefCheckId] = useState<string | null>(null);
  const [form] = Form.useForm<{ name: string; nameVi: string; code: string }>();
  const navigate = useNavigate();

  // Fetch all data — client-side search needs full dataset
  const { data, isLoading } = useFormTemplates(1, 9999);
  const createTemplate = useCreateTemplate();
  const deleteTemplate = useDeleteTemplate();

  // Pre-flight refs check: fetch refs eagerly on popconfirm open
  const { data: pendingRefs = [], isFetching: isFetchingRefs } = useBpReferencesForFormTemplate(bpRefCheckId);

  // Handle delete after confirming refs are empty
  const handleDeleteWithGuard = useCallback(
    (id: string) => {
      if (pendingRefs.length > 0) {
        // Refs exist — block and show modal
        return;
      }
      deleteTemplate.mutate({ id }, {
        onSuccess: () => appMessage.success(t('common.deleteSuccess', { defaultValue: 'Xóa thành công' })),
        onError: () => appMessage.error(t('common.deleteFailed', { defaultValue: 'Xóa thất bại' })),
      });
      setBpRefCheckId(null);
    },
    [pendingRefs, deleteTemplate, t],
  );

  // Bulk delete — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map(id => deleteTemplate.mutateAsync({ id })));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: 'Xóa thành công' }));
    }
    setSelectedIds([]);
  }

  const handleCreate = async () => {
    const values = await form.validateFields();
    try {
      await createTemplate.mutateAsync({
        name: values.name,
        nameVi: values.nameVi,
        code: values.code,
      });
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
      form.resetFields();
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
    }
  };

  const columns: ColumnsType<FormTemplateDto> = [
    {
      title: t('page.forms.col.code'),
      dataIndex: 'code',
      key: 'code',
      width: 120,
      render: (v: string) => <code>{v}</code>,
    },
    { title: t('page.forms.col.name'), dataIndex: 'name', key: 'name', ellipsis: true },
    {
      title: t('page.forms.col.status'),
      dataIndex: 'status',
      key: 'status',
      width: 130,
      render: (v: FormStatus) => (
        <Tag color={STATUS_COLOR[v]}>{t(`page.forms.status.${v}`)}</Tag>
      ),
    },
    {
      title: t('page.forms.col.fieldCount'),
      key: 'fieldCount',
      width: 100,
      render: (_: unknown, r: FormTemplateDto) => r.fields?.length ?? 0,
    },
    {
      title: t('page.forms.col.submissionsCount'),
      dataIndex: 'submissionsCount',
      key: 'submissionsCount',
      width: 100,
    },
    {
      title: t('page.forms.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 120,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: '',
      key: 'actions',
      width: 140,
      render: (_: unknown, r: FormTemplateDto) => (
        <Space size="small">
          <Tooltip title={t('common.edit', 'Chỉnh sửa')}>
            <Button size="small" icon={<EditOutlined />} onClick={() => navigate({ to: `/forms/${r.id}` })} />
          </Tooltip>
          {r.status === 'Draft' && (
            <Popconfirm
              title={
                bpRefCheckId === r.id && pendingRefs.length > 0
                  ? t('businessProcessAdmin.refs.cannotDelete', 'Mẫu biểu này đang được tham chiếu bởi quy trình')
                  : t('forms.deleteConfirm')
              }
              onOpenChange={(open) => {
                if (open) setBpRefCheckId(r.id);
                else if (!open && bpRefCheckId === r.id) setBpRefCheckId(null);
              }}
              onConfirm={() => {
                if (bpRefCheckId === r.id && pendingRefs.length > 0) {
                  // Block — show modal with full list
                  return;
                }
                handleDeleteWithGuard(r.id);
              }}
              okButtonProps={{
                disabled: bpRefCheckId === r.id && (isFetchingRefs || pendingRefs.length > 0),
                loading: bpRefCheckId === r.id && isFetchingRefs,
              }}
              okText={
                bpRefCheckId === r.id && pendingRefs.length > 0
                  ? t('businessProcessAdmin.refs.viewRefs', 'Xem chi tiết')
                  : t('common.confirm')
              }
              cancelText={t('common.cancel')}
            >
              <Button
                danger
                size="small"
                icon={<DeleteOutlined />}
                loading={deleteTemplate.isPending}
              />
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('page.forms.title')}
        stats={{ total: data?.items?.length ?? 0, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
            {t('forms.createTemplate')}
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
              okText={t('common.yes', { defaultValue: 'Có' })}
              cancelText={t('common.no', { defaultValue: 'Không' })}
            >
              <Button danger size="small">
                {t('common.deleteSelected', { defaultValue: `Xóa (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<FormTemplateDto>
          rowKey="id"
          columns={columns}
          dataSource={(data?.items ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (n) => t('page.forms.showTotal', { count: n }) }}
          locale={{ emptyText: t('page.forms.empty') }}
        />
      </AdminContentCard>

      <BpRefBlockModal
        open={bpRefCheckId !== null && pendingRefs.length > 0 && !isFetchingRefs}
        templateId={bpRefCheckId}
        onClose={() => setBpRefCheckId(null)}
      />

      <Modal
        title={t('forms.createTemplate')}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        confirmLoading={createTemplate.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="code"
            label="Code"
            validateFirst
            rules={[
              { required: true },
              // Mirrors BE CreateFormTemplateCommandValidator.cs — keep in sync
              { max: 100, message: t('common.validation.maxLength', { max: 100 }) },
              {
                pattern: /^[a-zA-Z_][a-zA-Z0-9_-]{0,99}$/,
                message: t('forms.codePatternError'),
              },
            ]}
          >
            <Input placeholder="e.g. FORM-001" />
          </Form.Item>
          <Form.Item
            name="name"
            label={t('forms.templateName') + ' (EN)'}
            validateFirst
            rules={[
              { required: true },
              // Mirrors BE CreateFormTemplateCommandValidator.cs — keep in sync
              { max: 200, message: t('common.validation.maxLength', { max: 200 }) },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="nameVi"
            label={t('forms.templateName') + ' (VI)'}
            validateFirst
            rules={[
              { required: true },
              // Mirrors BE CreateFormTemplateCommandValidator.cs — keep in sync
              { max: 200, message: t('common.validation.maxLength', { max: 200 }) },
            ]}
          >
            <Input />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
