// Modal for creating/editing a workflow transition

import { Modal, Form, Input, Select, AutoComplete, Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import type { WorkflowStateDto, WorkflowTransitionDto } from '../workflow-types';
import {
  useWorkflowActionTaxonomies,
  toTaxonomyOptions,
  isCustomCode,
} from '../use-workflow-taxonomies';

interface Props {
  open: boolean;
  transition?: WorkflowTransitionDto | null;
  /** Pre-fill fields when creating a new transition (e.g. draw-to-connect from canvas). Ignored if `transition` is provided. */
  initialFields?: { fromStateId?: string; toStateId?: string };
  states: WorkflowStateDto[];
  onSave: (values: Omit<WorkflowTransitionDto, 'id'> & { id?: string }) => void;
  onCancel: () => void;
}

export function TransitionEditorModal({ open, transition, initialFields, states, onSave, onCancel }: Props) {
  const { t } = useTranslation();
  const [form] = Form.useForm();
  const { data: actionTaxonomies = [] } = useWorkflowActionTaxonomies();
  const actionOptions = toTaxonomyOptions(actionTaxonomies);
  const watchedActionName = Form.useWatch('actionName', form);
  const showActionCustomWarning = isCustomCode(watchedActionName, actionTaxonomies);

  const handleOk = async () => {
    const values = await form.validateFields();
    onSave({ ...values, id: transition?.id });
    form.resetFields();
  };

  const stateOptions = states.map((s) => ({ label: s.displayNameVi || s.name, value: s.id }));

  return (
    <Modal
      title={transition ? t('workflow.editTransition') : t('workflow.addTransition')}
      open={open}
      onOk={handleOk}
      onCancel={() => { onCancel(); form.resetFields(); }}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      destroyOnHidden
      width={560}
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={transition ?? { ...initialFields, sortOrder: 0 }}
      >
        <div style={{ display: 'flex', gap: 16 }}>
          <Form.Item name="fromStateId" label={t('workflow.fromState')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <Select options={stateOptions} placeholder={t('workflow.selectState')} />
          </Form.Item>
          <Form.Item name="toStateId" label={t('workflow.toState')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <Select options={stateOptions} placeholder={t('workflow.selectState')} />
          </Form.Item>
        </div>
        <Form.Item
          name="actionName"
          label={t('workflow.actionName')}
          rules={[{ required: true }]}
          extra={
            showActionCustomWarning ? (
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
            placeholder={t('workflow.taxonomy.actionPlaceholder')}
            options={actionOptions}
            filterOption={(input, option) =>
              String(option?.label ?? '').toLowerCase().includes(input.toLowerCase()) ||
              String(option?.value ?? '').toLowerCase().includes(input.toLowerCase())
            }
          />
        </Form.Item>
        <div style={{ display: 'flex', gap: 16 }}>
          <Form.Item name="actionLabelVi" label={t('workflow.actionLabelVi')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <Input placeholder="Nhãn tiếng Việt" />
          </Form.Item>
          <Form.Item name="actionLabelEn" label={t('workflow.actionLabelEn')} rules={[{ required: true }]} style={{ flex: 1 }}>
            <Input placeholder="English label" />
          </Form.Item>
        </div>
        <Form.Item name="conditionsJson" label={t('workflow.conditions')}>
          <Input.TextArea rows={3} placeholder='{"operator":"equals","field":"status","value":"ready"}' />
        </Form.Item>
        <Form.Item
          name="defaultAssigneeId"
          label={t('workflow.defaultAssigneeId', 'Người phê duyệt mặc định')}
        >
          <Input placeholder="UUID người phê duyệt (tuỳ chọn)" />
        </Form.Item>
      </Form>
    </Modal>
  );
}
