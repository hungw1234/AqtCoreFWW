// Assignment rules tab — lists WorkflowAssignmentRule rows.
// Two modes:
//   - standalone admin page passes NO definitionId → global list (all definitions), create handled
//     by that page's own toolbar button.
//   - definition-detail tab passes definitionId → list is scoped to that definition AND an inline
//     "+ add" button opens a create modal pinned to the current definition (no definition picker).

import { useState } from 'react';
import { Table, Tag, Button, Popconfirm, Modal, Form, Input, InputNumber, DatePicker, Flex, Typography, Empty } from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useAssignmentRules, useDeleteAssignmentRule, useCreateAssignmentRule } from '../workflow-api';
import type { WorkflowAssignmentRuleDto } from '../workflow-types';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import { extractApiErrorMessage } from '@/core/api';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

interface DefinitionAssignmentRulesTabProps {
  /** When set, scope the list to this definition (PublicId) and enable an inline pinned-create button. */
  definitionId?: string;
}

export function DefinitionAssignmentRulesTab({ definitionId }: DefinitionAssignmentRulesTabProps = {}) {
  const { t } = useTranslation();
  const { data: rules, isLoading } = useAssignmentRules();
  const deleteMutation = useDeleteAssignmentRule();
  const createMutation = useCreateAssignmentRule();
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();

  // Scope to the current definition when embedded in the definition-detail tab; global otherwise.
  // DTO.workflowDefinitionId is the definition PublicId (resolved BE-side via JOIN), same value as
  // the route param — so a client-side filter is exact and needs no extra BE query.
  const visibleRules = definitionId
    ? (rules ?? []).filter((r) => r.workflowDefinitionId === definitionId)
    : (rules ?? []);

  const handleDelete = async (id: string) => {
    // Was un-guarded: a rejected delete became a silent unhandled rejection (no toast, no log).
    try {
      await deleteMutation.mutateAsync(id);
      appMessage.success(t('common.deleted'));
    } catch (err) {
      appMessage.error(extractApiErrorMessage(err) ?? t('common.error'));
    }
  };

  const handleCreate = async () => {
    // Validation rejects render inline on the form — return silently, no toast noise.
    let values: Awaited<ReturnType<typeof form.validateFields>>;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    try {
      await createMutation.mutateAsync({
        // Pinned to the current definition — the detail-tab create never picks another definition.
        workflowDefinitionId: definitionId!,
        entityType: values.entityType?.trim() || null,
        entitySubType: values.entitySubType?.trim() || null,
        priority: values.priority,
        description: values.description?.trim() || null,
        effectiveFrom: values.effectiveFrom?.toISOString() ?? new Date().toISOString(),
        effectiveUntil: values.effectiveUntil?.toISOString() ?? null,
      });
      appMessage.success(t('common.saved'));
      setModalOpen(false);
      form.resetFields();
    } catch (err) {
      appMessage.error(extractApiErrorMessage(err) ?? t('common.error'));
    }
  };

  const columns: ColumnsType<WorkflowAssignmentRuleDto> = [
    { title: t('workflow.entityType'), dataIndex: 'entityType', render: (v: string | null) => v || '*' },
    { title: t('workflow.entitySubType'), dataIndex: 'entitySubType', render: (v: string | null) => v || '*' },
    {
      title: t('workflow.specificity'),
      dataIndex: 'specificity',
      width: 100,
      render: (v: number) => <Tag color={v >= 3 ? 'green' : v >= 2 ? 'blue' : 'default'}>{v}</Tag>,
    },
    { title: t('workflow.priority'), dataIndex: 'priority', width: 80 },
    { title: t('workflow.description'), dataIndex: 'description', ellipsis: true },
    {
      title: t('workflow.effectiveFrom'),
      dataIndex: 'effectiveFrom',
      width: 120,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: t('common.actions'),
      width: 60,
      render: (_: unknown, r: WorkflowAssignmentRuleDto) => (
        <Popconfirm title={t('common.confirmDelete')} onConfirm={() => handleDelete(r.id)}>
          <Button size="small" danger icon={<DeleteOutlined />} />
        </Popconfirm>
      ),
    },
  ];

  return (
    <AdminContentCard noPadding>
      {/* Internal toolbar only in the definition-detail context; the standalone page supplies its own. */}
      {definitionId && (
        <Flex
          justify="space-between"
          align="center"
          gap={12}
          wrap
          className="admin-table-toolbar"
          style={{ padding: '14px 16px' }}
        >
          <Text strong>{t('workflow.assignmentRules', { defaultValue: 'Quy tắc gán quy trình' })}</Text>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
            {t('workflow.addRule')}
          </Button>
        </Flex>
      )}

      <Table<WorkflowAssignmentRuleDto>
        rowKey="id"
        columns={columns}
        dataSource={visibleRules}
        size="small"
        pagination={false}
        loading={isLoading}
        locale={{
          emptyText: (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={t('workflow.emptyAssignmentRules', { defaultValue: 'Chưa có quy tắc gán quy trình' })}
            />
          ),
        }}
      />

      {definitionId && (
        <Modal
          title={t('workflow.addRule')}
          open={modalOpen}
          onOk={handleCreate}
          onCancel={() => { setModalOpen(false); form.resetFields(); }}
          confirmLoading={createMutation.isPending}
          okText={t('common.save')}
          cancelText={t('common.cancel')}
          destroyOnHidden
        >
          {/* Definition is fixed to the page context — no picker (unlike the standalone admin page). */}
          <Form form={form} layout="vertical">
            <div style={{ display: 'flex', gap: 16 }}>
              <Form.Item name="entityType" label={t('workflow.entityType')} style={{ flex: 1 }}>
                <Input placeholder="* = tất cả" />
              </Form.Item>
              <Form.Item name="entitySubType" label={t('workflow.entitySubType')} style={{ flex: 1 }}>
                <Input placeholder="* = tất cả" />
              </Form.Item>
            </div>
            <Form.Item name="priority" label={t('workflow.priority')} initialValue={0} rules={[{ required: true }]}>
              <InputNumber min={0} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="description" label={t('workflow.description')}>
              <Input.TextArea rows={2} />
            </Form.Item>
            <div style={{ display: 'flex', gap: 16 }}>
              <Form.Item name="effectiveFrom" label={t('workflow.effectiveFrom')} style={{ flex: 1 }}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
              <Form.Item name="effectiveUntil" label={t('workflow.effectiveUntil')} style={{ flex: 1 }}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </div>
          </Form>
        </Modal>
      )}
    </AdminContentCard>
  );
}
