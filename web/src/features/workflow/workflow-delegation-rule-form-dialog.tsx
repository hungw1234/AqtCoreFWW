// Delegation rule create/edit dialog — used by DelegationRulesPage
// Handles both create (no rule prop) and edit (rule prop provided) flows.

import { useEffect } from 'react';
import { Modal, Form, Input, DatePicker } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useCreateDelegationRule, useUpdateDelegationRule } from './workflow-api';
import type { DelegationRuleDto } from './workflow-types';
import { appMessage } from '@/core/api/notification-bridge';

interface FormValues {
  delegatorUserId: string;
  delegateUserId: string;
  validFrom: dayjs.Dayjs;
  validUntil: dayjs.Dayjs;
}

interface Props {
  open: boolean;
  editRule?: DelegationRuleDto | null;
  onClose: () => void;
}

export function DelegationRuleFormDialog({ open, editRule, onClose }: Props) {
  const { t } = useTranslation();
  const [form] = Form.useForm<FormValues>();
  const createMutation = useCreateDelegationRule();
  const updateMutation = useUpdateDelegationRule();
  const isEdit = !!editRule;

  // Populate form when editing
  useEffect(() => {
    if (open && editRule) {
      form.setFieldsValue({
        delegatorUserId: editRule.delegatorUserId,
        delegateUserId: editRule.delegateUserId,
        validFrom: dayjs(editRule.validFrom),
        validUntil: dayjs(editRule.validUntil),
      });
    } else if (open) {
      form.resetFields();
    }
  }, [open, editRule, form]);

  const handleOk = async () => {
    const values = await form.validateFields();
    const validFrom = values.validFrom.toISOString();
    const validUntil = values.validUntil.toISOString();

    if (isEdit && editRule) {
      await updateMutation.mutateAsync({
        id: editRule.id,
        body: { delegateUserId: values.delegateUserId, validFrom, validUntil },
      });
      appMessage.success(t('common.saved'));
    } else {
      await createMutation.mutateAsync({
        delegatorUserId: values.delegatorUserId,
        delegateUserId: values.delegateUserId,
        validFrom,
        validUntil,
      });
      appMessage.success(t('common.saved'));
    }
    onClose();
  };

  const isPending = createMutation.isPending || updateMutation.isPending;

  return (
    <Modal
      title={isEdit ? t('workflow.delegation.editRule') : t('workflow.delegation.addRule')}
      open={open}
      onOk={handleOk}
      onCancel={onClose}
      confirmLoading={isPending}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      destroyOnHidden
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="delegatorUserId"
          label={t('workflow.delegation.delegatorUserId')}
          rules={[{ required: true, message: t('workflow.delegation.requiredField') }]}
        >
          <Input placeholder={t('workflow.delegation.userIdPlaceholder')} disabled={isEdit} />
        </Form.Item>
        <Form.Item
          name="delegateUserId"
          label={t('workflow.delegation.delegateUserId')}
          rules={[{ required: true, message: t('workflow.delegation.requiredField') }]}
        >
          <Input placeholder={t('workflow.delegation.userIdPlaceholder')} />
        </Form.Item>
        <div style={{ display: 'flex', gap: 16 }}>
          <Form.Item
            name="validFrom"
            label={t('workflow.delegation.validFrom')}
            rules={[{ required: true, message: t('workflow.delegation.requiredField') }]}
            style={{ flex: 1 }}
          >
            <DatePicker showTime style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="validUntil"
            label={t('workflow.delegation.validUntil')}
            rules={[{ required: true, message: t('workflow.delegation.requiredField') }]}
            style={{ flex: 1 }}
          >
            <DatePicker showTime style={{ width: '100%' }} />
          </Form.Item>
        </div>
      </Form>
    </Modal>
  );
}
