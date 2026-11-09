// Standalone page for managing workflow assignment rules across all definitions

import { useState } from 'react';
import { Typography, Button, Space, Modal, Form, Input, InputNumber, Select, DatePicker } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useWorkflowDefinitions, useCreateAssignmentRule } from './workflow-api';
import { DefinitionAssignmentRulesTab } from './components/definition-assignment-rules-tab';
import { appMessage } from '@/core/api/notification-bridge';

const { Title } = Typography;

export function WorkflowAssignmentRulesPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();
  const { data: definitions } = useWorkflowDefinitions(1);
  const createMutation = useCreateAssignmentRule();

  const handleCreate = async () => {
    const values = await form.validateFields();
    await createMutation.mutateAsync({
      ...values,
      effectiveFrom: values.effectiveFrom?.toISOString() ?? new Date().toISOString(),
      effectiveUntil: values.effectiveUntil?.toISOString() ?? null,
    });
    appMessage.success(t('common.saved'));
    setModalOpen(false);
    form.resetFields();
  };

  const defOptions = (definitions?.items ?? []).map((d) => ({
    label: `${d.name} (v${d.version})`,
    value: d.id,
  }));

  return (
    <div>
      <Space style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between' }}>
        <Title level={4} style={{ margin: 0 }}>{t('workflow.assignmentRules')}</Title>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
          {t('workflow.addRule')}
        </Button>
      </Space>

      <DefinitionAssignmentRulesTab />

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
        <Form form={form} layout="vertical">
          <Form.Item name="workflowDefinitionId" label={t('workflow.definition')} rules={[{ required: true }]}>
            <Select options={defOptions} placeholder={t('workflow.selectDefinition')} showSearch optionFilterProp="label" />
          </Form.Item>
          <div style={{ display: 'flex', gap: 16 }}>
            <Form.Item name="entityType" label={t('workflow.entityType')} style={{ flex: 1 }}>
              <Input placeholder="* = tất cả" />
            </Form.Item>
            <Form.Item name="entitySubType" label={t('workflow.entitySubType')} style={{ flex: 1 }}>
              <Input placeholder="* = tất cả" />
            </Form.Item>
          </div>
          <Form.Item name="priority" label={t('workflow.priority')} rules={[{ required: true }]}>
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
    </div>
  );
}
