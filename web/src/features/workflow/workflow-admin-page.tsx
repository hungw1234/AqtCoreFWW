// WorkflowAdminPage — enhanced list with version badge, status, clone/delete actions

import { useState } from 'react';
import { Table, Button, Space, Modal, Form, Input, Tag, Popconfirm, Alert, List, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, CopyOutlined, DeleteOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useWorkflowDefinitions,
  useCreateWorkflow,
  useDeleteWorkflowDefinition,
} from './workflow-api';
import { useBpReferencesForWorkflowDefinition } from '@/features/business-process/business-process-admin-api';
import type { WorkflowDefinitionListDto, CreateWorkflowRequest } from './workflow-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';

// BpRefBlockModal for workflow definition delete guard
function WfBpRefBlockModal({
  open,
  definitionId,
  onClose,
}: {
  open: boolean;
  definitionId: string | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { data: refs = [], isLoading } = useBpReferencesForWorkflowDefinition(open ? definitionId : null);
  return (
    <Modal
      open={open}
      title={
        <Space>
          <ExclamationCircleOutlined style={{ color: '#faad14' }} />
          {t('businessProcessAdmin.refs.title', 'Không thể xóa workflow')}
        </Space>
      }
      onCancel={onClose}
      footer={<Button onClick={onClose}>{t('common.cancel', 'Đóng')}</Button>}
      destroyOnHidden
    >
      <Alert
        type="warning"
        message={t('businessProcessAdmin.refs.cannotDelete', 'Workflow này đang được sử dụng bởi các quy trình sau. Hãy xóa các tham chiếu trước khi xóa.')}
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
              {!ref.isActive && <Tag color="default">Inactive</Tag>}
            </Space>
          </List.Item>
        )}
      />
    </Modal>
  );
}

export function WorkflowAdminPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [bpRefCheckWfId, setBpRefCheckWfId] = useState<string | null>(null);
  const [form] = Form.useForm<CreateWorkflowRequest>();

  // Debounce search input by 300ms — prevents API call on every keystroke
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  // useWorkflowDefinitions uses page/pageSize positional args (not pageNumber)
  const { page, pageSize, antPagination } = useServerPagination(20, [debouncedSearch]);

  const { data, isLoading } = useWorkflowDefinitions(page, pageSize, debouncedSearch || undefined);
  const total = data?.totalCount ?? 0;
  const createWorkflow = useCreateWorkflow();
  const deleteMutation = useDeleteWorkflowDefinition();

  // Pre-flight BP refs check for delete guard
  const { data: pendingWfRefs = [], isFetching: isFetchingWfRefs } = useBpReferencesForWorkflowDefinition(bpRefCheckWfId);

  const handleCreate = async () => {
    const values = await form.validateFields();
    try {
      // entityType defaults to "Case" — only entity type today; expand to dropdown when entity #2 exists (D4)
      const created = await createWorkflow.mutateAsync({ ...values, entityType: 'Case', states: [], transitions: [] });
      appMessage.success(t('workflow.createSuccess', 'Tạo workflow thành công'));
      form.resetFields();
      setModalOpen(false);
      navigate({ to: '/admin/workflow/$definitionId', params: { definitionId: created.id } });
    } catch {
      appMessage.error(t('workflow.createError', 'Tạo workflow thất bại'));
    }
  };

  const handleDelete = async (id: string) => {
    if (pendingWfRefs.length > 0) return; // blocked — modal will show
    try {
      await deleteMutation.mutateAsync(id);
      appMessage.success(t('common.deleted'));
    } catch {
      appMessage.error(t('common.error', 'Thao tác thất bại'));
    }
    setBpRefCheckWfId(null);
  };

  const columns: ColumnsType<WorkflowDefinitionListDto> = [
    {
      title: t('workflow.col.name'),
      dataIndex: 'name',
      ellipsis: true,
      render: (name: string, r) => (
        <a onClick={() => navigate({ to: '/admin/workflow/$definitionId', params: { definitionId: r.id } })}>
          {name}
        </a>
      ),
    },
    {
      title: t('workflow.status'),
      width: 100,
      render: (_: unknown, r: WorkflowDefinitionListDto) => (
        <Tag color={r.isActive ? 'green' : 'default'}>
          {r.isActive ? t('workflow.active') : t('workflow.inactive')}
        </Tag>
      ),
    },
    {
      title: t('workflow.version'),
      width: 80,
      render: (_: unknown, r: WorkflowDefinitionListDto) => (
        <Space size={4}>
          <span>v{r.version}</span>
          {r.isLatest && <Tag color="blue" style={{ fontSize: 10 }}>latest</Tag>}
        </Space>
      ),
    },
    { title: t('workflow.col.states'), dataIndex: 'stateCount', width: 70, align: 'center' },
    { title: t('workflow.col.transitions'), dataIndex: 'transitionCount', width: 90, align: 'center' },
    {
      title: t('workflow.col.createdAt'),
      dataIndex: 'createdAt',
      width: 110,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: t('common.actions'),
      width: 90,
      render: (_: unknown, r: WorkflowDefinitionListDto) => (
        <Space>
          <Button
            size="small"
            icon={<CopyOutlined />}
            title={t('workflow.clone')}
            onClick={() => navigate({ to: '/admin/workflow/$definitionId', params: { definitionId: r.id } })}
          />
          <Popconfirm
            title={
              bpRefCheckWfId === r.id && pendingWfRefs.length > 0
                ? t('businessProcessAdmin.refs.cannotDelete', 'Workflow này đang được tham chiếu bởi quy trình')
                : t('common.confirmDelete')
            }
            onOpenChange={(open) => {
              if (open) setBpRefCheckWfId(r.id);
              else if (!open && bpRefCheckWfId === r.id) setBpRefCheckWfId(null);
            }}
            onConfirm={() => handleDelete(r.id)}
            okButtonProps={{
              disabled: bpRefCheckWfId === r.id && (isFetchingWfRefs || pendingWfRefs.length > 0),
              loading: bpRefCheckWfId === r.id && isFetchingWfRefs,
            }}
            cancelText={t('common.cancel')}
          >
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('workflow.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
            {t('workflow.createTitle')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchInput}
          onSearchChange={setSearchInput}
        />
        <Table<WorkflowDefinitionListDto>
          rowKey="id"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={isLoading}
          size="small"

          pagination={{ ...antPagination, total }}
        />
      </AdminContentCard>

      <WfBpRefBlockModal
        open={bpRefCheckWfId !== null && pendingWfRefs.length > 0 && !isFetchingWfRefs}
        definitionId={bpRefCheckWfId}
        onClose={() => setBpRefCheckWfId(null)}
      />

      <Modal
        title={t('workflow.createTitle')}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        confirmLoading={createWorkflow.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="name" label={t('workflow.col.name')} rules={[{ required: true }]}>
            <Input placeholder={t('workflow.namePlaceholder')} />
          </Form.Item>
          {/* entityType fixed to "Case" — only entity type supported today; promote to dropdown when entity #2 exists (D4) */}
          <Form.Item name="entityType" label={t('workflow.col.entityType')} initialValue="Case">
            <Input disabled />
          </Form.Item>
          <Form.Item name="description" label={t('workflow.description')}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
