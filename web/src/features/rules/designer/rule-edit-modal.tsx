// Modal for editing a single rule's fields from the visual designer
// Props: rule (current data), open, onSave (changed fields only), onCancel, isReadOnly

import { useEffect } from 'react';
import { Modal, Form, Input, Switch, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import type { RuleDto, UpdateRuleDto } from '../rules-api';

const { TextArea } = Input;

const FAILURE_ACTION_OPTIONS = [
  { label: 'Throw', value: 'Throw' },
  { label: 'Skip', value: 'Skip' },
  { label: 'Default', value: 'Default' },
];

interface RuleEditModalProps {
  rule: RuleDto | null;
  open: boolean;
  isReadOnly: boolean;
  onSave: (ruleId: string, changes: Partial<UpdateRuleDto>) => void;
  onCancel: () => void;
}

// RuleEditModal — edit rule name, expression, enabled, failureAction
export function RuleEditModal({ rule, open, isReadOnly, onSave, onCancel }: RuleEditModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<UpdateRuleDto>();

  // Reset form when rule changes
  useEffect(() => {
    if (rule) {
      form.setFieldsValue({
        name: rule.name,
        expression: rule.expression,
        enabled: rule.enabled,
        failureAction: rule.failureAction,
      });
    }
  }, [rule, form]);

  function handleOk() {
    if (isReadOnly || !rule) {
      onCancel();
      return;
    }
    form.validateFields().then((values) => {
      // Diff against original — only send changed fields
      const changes: Partial<UpdateRuleDto> = {};
      if (values.name !== rule.name) changes.name = values.name;
      if (values.expression !== rule.expression) changes.expression = values.expression;
      if (values.enabled !== rule.enabled) changes.enabled = values.enabled;
      if (values.failureAction !== rule.failureAction) changes.failureAction = values.failureAction;

      if (Object.keys(changes).length > 0) {
        onSave(rule.id, changes);
      }
      onCancel();
    });
  }

  return (
    <Modal
      title={isReadOnly ? rule?.name : t('rules.designer.editRule')}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      okButtonProps={{ disabled: isReadOnly }}
      width={520}
      destroyOnHidden={false}
    >
      {isReadOnly && (
        <p style={{ color: '#888', marginBottom: 12, fontSize: 12 }}>
          {t('rules.designer.readOnly')}
        </p>
      )}
      <Form form={form} layout="vertical" disabled={isReadOnly}>
        <Form.Item name="name" label={t('rules.col.ruleName')} rules={[{ required: true }]}>
          <Input />
        </Form.Item>

        <Form.Item name="expression" label={t('rules.designer.expression')}>
          <TextArea
            rows={6}
            style={{ fontFamily: 'monospace', fontSize: 12 }}
            placeholder="e.g. input.age > 18 && input.score >= 700"
          />
        </Form.Item>

        <Form.Item name="enabled" label={t('rules.designer.enabled')} valuePropName="checked">
          <Switch />
        </Form.Item>

        <Form.Item name="failureAction" label={t('rules.designer.failureAction')}>
          <Select options={FAILURE_ACTION_OPTIONS} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
