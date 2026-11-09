// Phase-00 Chunk 4b-2 + 4c — Workflow Action Taxonomy admin CRUD page.
// Mirror of workflow-state-taxonomy-admin-page minus Category/Color fields.
// C4c adds: Archived tab, Restore, Deprecate dialog.

import { useState } from 'react';
import {
  Table,
  Button,
  Space,
  Drawer,
  Form,
  Input,
  InputNumber,
  Tabs,
  Popconfirm,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined, UndoOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  useWorkflowActionTaxonomies,
  useCreateWorkflowActionTaxonomy,
  useUpdateWorkflowActionTaxonomy,
  useDeleteWorkflowActionTaxonomy,
  useRestoreWorkflowActionTaxonomy,
  useArchivedWorkflowActionTaxonomies,
  type WorkflowActionTaxonomyDto,
} from './use-workflow-taxonomies';
import { extractApiErrorCode } from '@/core/api';
import { TaxonomyDeprecateDialog } from './taxonomy-deprecate-dialog';
import { appMessage } from '@/core/api/notification-bridge';

interface FormValues {
  code: string;
  nameVi: string;
  nameEn: string;
  sortOrder: number;
  i18nKey?: string;
  icon?: string;
}

export function WorkflowActionTaxonomyAdminPage() {
  const { t } = useTranslation();

  const { data: activeRows = [], isLoading: activeLoading } = useWorkflowActionTaxonomies();
  const { data: archivedRows = [], isLoading: archivedLoading } = useArchivedWorkflowActionTaxonomies();
  const createMutation = useCreateWorkflowActionTaxonomy();
  const updateMutation = useUpdateWorkflowActionTaxonomy();
  const deleteMutation = useDeleteWorkflowActionTaxonomy();
  const restoreMutation = useRestoreWorkflowActionTaxonomy();

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [editing, setEditing] = useState<WorkflowActionTaxonomyDto | null>(null);
  const [deprecating, setDeprecating] = useState<WorkflowActionTaxonomyDto | null>(null);
  const [form] = Form.useForm<FormValues>();

  const isEdit = editing !== null;

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ sortOrder: 0 });
    setDrawerOpen(true);
  };

  const openEdit = (row: WorkflowActionTaxonomyDto) => {
    setEditing(row);
    form.resetFields();
    form.setFieldsValue({
      code: row.code,
      nameVi: row.nameVi,
      nameEn: row.nameEn,
      sortOrder: row.sortOrder,
      i18nKey: row.i18nKey ?? undefined,
      icon: row.icon ?? undefined,
    });
    setDrawerOpen(true);
  };

  const closeDrawer = () => {
    setDrawerOpen(false);
    setEditing(null);
    form.resetFields();
  };

  const handleSubmit = async () => {
    const values = await form.validateFields();
    try {
      if (isEdit && editing) {
        await updateMutation.mutateAsync({
          id: editing.id,
          nameVi: values.nameVi,
          nameEn: values.nameEn,
          sortOrder: values.sortOrder,
          i18nKey: values.i18nKey ?? null,
          icon: values.icon ?? null,
          rowVersion: editing.rowVersion,
        });
      } else {
        await createMutation.mutateAsync({
          code: values.code,
          nameVi: values.nameVi,
          nameEn: values.nameEn,
          sortOrder: values.sortOrder,
          i18nKey: values.i18nKey ?? null,
          icon: values.icon ?? null,
        });
      }
      appMessage.success(t('common.success'));
      closeDrawer();
    } catch (err: unknown) {
      const errorCode = extractApiErrorCode(err);
      if (errorCode === 'GOV_CONCURRENCY_CONFLICT') {
        appMessage.error(t('workflow.taxonomy.admin.concurrencyConflict'));
      } else {
        appMessage.error(t('common.error'));
      }
    }
  };

  const handleDeprecate = async (payload: { confirmCode: string; reason: string }) => {
    if (!deprecating) return;
    try {
      await deleteMutation.mutateAsync({ id: deprecating.id, ...payload });
      appMessage.success(t('common.success'));
      setDeprecating(null);
    } catch (err: unknown) {
      const code = extractApiErrorCode(err);
      appMessage.error(
        code === 'GOV_CONFIRM_CODE_MISMATCH'
          ? t('workflow.taxonomy.admin.confirmCodeError')
          : t('common.error'),
      );
    }
  };

  const handleRestore = async (row: WorkflowActionTaxonomyDto) => {
    try {
      await restoreMutation.mutateAsync(row.id);
      appMessage.success(t('common.success'));
    } catch (err: unknown) {
      const code = extractApiErrorCode(err);
      appMessage.error(
        code === 'GOV_ALREADY_ACTIVE'
          ? t('workflow.taxonomy.admin.alreadyActive')
          : t('common.error'),
      );
    }
  };

  const baseColumns: ColumnsType<WorkflowActionTaxonomyDto> = [
    { title: t('workflow.taxonomy.admin.code'), dataIndex: 'code', width: 140 },
    { title: t('workflow.taxonomy.admin.nameVi'), dataIndex: 'nameVi' },
    { title: t('workflow.taxonomy.admin.nameEn'), dataIndex: 'nameEn' },
    { title: t('workflow.taxonomy.admin.sortOrder'), dataIndex: 'sortOrder', width: 80 },
  ];

  const activeColumns: ColumnsType<WorkflowActionTaxonomyDto> = [
    ...baseColumns,
    {
      title: t('common.actions'),
      width: 180,
      render: (_: unknown, row: WorkflowActionTaxonomyDto) => (
        <Space>
          <Button
            size="small"
            icon={<EditOutlined />}
            aria-label={t('workflow.taxonomy.admin.edit')}
            onClick={() => openEdit(row)}
          >
            {t('workflow.taxonomy.admin.edit')}
          </Button>
          <Button
            size="small"
            danger
            icon={<DeleteOutlined />}
            aria-label={t('workflow.taxonomy.admin.deprecate')}
            onClick={() => setDeprecating(row)}
          >
            {t('workflow.taxonomy.admin.deprecate')}
          </Button>
        </Space>
      ),
    },
  ];

  const archivedColumns: ColumnsType<WorkflowActionTaxonomyDto> = [
    ...baseColumns,
    {
      title: t('workflow.taxonomy.admin.reasonLabel'),
      dataIndex: 'deprecatedReason',
      ellipsis: true,
    },
    {
      title: t('common.actions'),
      width: 130,
      render: (_: unknown, row: WorkflowActionTaxonomyDto) => (
        <Popconfirm
          title={t('workflow.taxonomy.admin.restore')}
          onConfirm={() => handleRestore(row)}
          okText={t('workflow.taxonomy.admin.restore')}
          cancelText={t('common.cancel')}
        >
          <Button
            size="small"
            type="primary"
            ghost
            icon={<UndoOutlined />}
            aria-label={t('workflow.taxonomy.admin.restore')}
          >
            {t('workflow.taxonomy.admin.restore')}
          </Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <div style={{ padding: 24 }}>
      <Space style={{ marginBottom: 16, justifyContent: 'space-between', width: '100%' }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('workflow.taxonomy.admin.actionsTitle')}
        </Typography.Title>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          {t('workflow.taxonomy.admin.create')}
        </Button>
      </Space>

      <Tabs
        defaultActiveKey="active"
        items={[
          {
            key: 'active',
            label: t('workflow.taxonomy.admin.tabActive'),
            children: (
              <Table
                rowKey="id"
                size="middle"
                dataSource={activeRows}
                columns={activeColumns}
                loading={activeLoading}
                pagination={{ pageSize: 20 }}
              />
            ),
          },
          {
            key: 'archived',
            label: t('workflow.taxonomy.admin.tabArchived'),
            children: (
              <Table
                rowKey="id"
                size="middle"
                dataSource={archivedRows}
                columns={archivedColumns}
                loading={archivedLoading}
                pagination={{ pageSize: 20 }}
              />
            ),
          },
        ]}
      />

      <Drawer
        title={isEdit ? t('workflow.taxonomy.admin.editActionTitle') : t('workflow.taxonomy.admin.createActionTitle')}
        open={drawerOpen}
        onClose={closeDrawer}
        width={520}
        destroyOnClose
        extra={
          <Space>
            <Button onClick={closeDrawer}>{t('common.cancel')}</Button>
            <Button
              type="primary"
              onClick={handleSubmit}
              loading={createMutation.isPending || updateMutation.isPending}
            >
              {t('common.save')}
            </Button>
          </Space>
        }
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="code"
            label={t('workflow.taxonomy.admin.code')}
            rules={[
              { required: true },
              { pattern: /^[A-Za-z][A-Za-z0-9_]*$/, message: t('workflow.taxonomy.admin.codePattern') },
              { max: 64 },
            ]}
          >
            <Input disabled={isEdit} placeholder="Submit" />
          </Form.Item>

          <Form.Item name="nameVi" label={t('workflow.taxonomy.admin.nameVi')} rules={[{ required: true, max: 200 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="nameEn" label={t('workflow.taxonomy.admin.nameEn')} rules={[{ required: true, max: 200 }]}>
            <Input />
          </Form.Item>

          <Form.Item name="sortOrder" label={t('workflow.taxonomy.admin.sortOrder')} rules={[{ required: true }]}>
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>

          <Form.Item name="i18nKey" label="I18n Key" rules={[{ max: 100 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="icon" label={t('workflow.taxonomy.admin.icon')} rules={[{ max: 50 }]}>
            <Input />
          </Form.Item>
        </Form>
      </Drawer>

      <TaxonomyDeprecateDialog
        open={deprecating !== null}
        code={deprecating?.code ?? ''}
        confirmLoading={deleteMutation.isPending}
        onConfirm={handleDeprecate}
        onCancel={() => setDeprecating(null)}
      />
    </div>
  );
}
