// Modal for creating/editing a workflow state

import { Modal, Form, Input, Switch, InputNumber, ColorPicker, AutoComplete, Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import type { WorkflowStateDto } from '../workflow-types';
import {
  useWorkflowStateTaxonomies,
  toTaxonomyOptions,
  isCustomCode,
} from '../use-workflow-taxonomies';

interface Props {
  open: boolean;
  state?: WorkflowStateDto | null;
  onSave: (values: Omit<WorkflowStateDto, 'id'> & { id?: string }) => void;
  onCancel: () => void;
}

export function StateEditorModal({ open, state, onSave, onCancel }: Props) {
  const { t } = useTranslation();
  const [form] = Form.useForm();
  const isEdit = !!state;
  const { data: taxonomies = [] } = useWorkflowStateTaxonomies();
  const options = toTaxonomyOptions(taxonomies);
  // Track the current name field to drive the "custom code" warning without forcing re-renders elsewhere
  const watchedName = Form.useWatch('name', form);
  const showCustomWarning = isCustomCode(watchedName, taxonomies);

  const handleOk = async () => {
    const values = await form.validateFields();
    const color = typeof values.color === 'string' ? values.color : values.color?.toHexString?.() ?? '#808080';
    onSave({ ...values, color, id: state?.id });
    form.resetFields();
  };

  return (
    <Modal
      title={isEdit ? t('workflow.editState') : t('workflow.addState')}
      open={open}
      onOk={handleOk}
      onCancel={() => { onCancel(); form.resetFields(); }}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={state ?? { color: '#808080', sortOrder: 0, isInitial: false, isFinal: false }}
      >
        <Form.Item
          name="name"
          label={t('workflow.stateName')}
          rules={[{ required: true }]}
          extra={
            showCustomWarning ? (
              <Alert
                type="warning"
                showIcon
                message={t('workflow.taxonomy.customCodeWarning')}
                style={{ marginTop: 4 }}
              />
            ) : null
          }
        >
          <AutoComplete
            allowClear
            placeholder={t('workflow.taxonomy.statePlaceholder')}
            options={options}
            // Case-insensitive label/value match so typing "draft" surfaces "Draft — Nháp"
            filterOption={(input, option) =>
              String(option?.label ?? '').toLowerCase().includes(input.toLowerCase()) ||
              String(option?.value ?? '').toLowerCase().includes(input.toLowerCase())
            }
          />
        </Form.Item>
        <Form.Item name="displayNameVi" label={t('workflow.displayNameVi')} rules={[{ required: true }]}>
          <Input placeholder="Tên hiển thị tiếng Việt" />
        </Form.Item>
        <Form.Item name="displayNameEn" label={t('workflow.displayNameEn')} rules={[{ required: true }]}>
          <Input placeholder="English display name" />
        </Form.Item>
        <div style={{ display: 'flex', gap: 16 }}>
          <Form.Item name="isInitial" label={t('workflow.isInitial')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="isFinal" label={t('workflow.isFinal')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </div>
        <div style={{ display: 'flex', gap: 16 }}>
          <Form.Item name="color" label={t('workflow.color')}>
            <ColorPicker />
          </Form.Item>
          <Form.Item name="sortOrder" label={t('workflow.sortOrder')}>
            <InputNumber min={0} />
          </Form.Item>
        </div>
        <Form.Item
          name="slaHours"
          label={t('workflow.slaHours', 'SLA (hours)')}
          extra={t('workflow.slaHoursHint', 'Leave blank for no per-state SLA')}
        >
          <InputNumber min={1} placeholder="e.g. 24" style={{ width: '100%' }} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
