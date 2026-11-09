// Delegate task dialog — modal for delegating a task to another user with expiry and reason.
// Opened from TaskDetailDrawer when user clicks the "Delegate" button.

import { Modal, Form, Input, DatePicker } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDelegateTask } from './workflow-api';
import { appMessage } from '@/core/api/notification-bridge';

interface FormValues {
  delegateToUserId: string;
  expiresAt: import('dayjs').Dayjs;
  reason?: string;
}

interface Props {
  taskId: string;
  open: boolean;
  onClose: () => void;
}

export function DelegateTaskDialog({ taskId, open, onClose }: Props) {
  const { t } = useTranslation();
  const [form] = Form.useForm<FormValues>();
  const delegateMutation = useDelegateTask();

  const handleOk = async () => {
    const values = await form.validateFields();
    await delegateMutation.mutateAsync({
      taskId,
      body: {
        delegateToUserId: values.delegateToUserId,
        expiresAt: values.expiresAt.toISOString(),
        reason: values.reason ?? null,
      },
    });
    appMessage.success(t('workflow.delegation.delegateSuccess'));
    form.resetFields();
    onClose();
  };

  const handleCancel = () => {
    form.resetFields();
    onClose();
  };

  return (
    <Modal
      title={t('workflow.delegation.delegateTask')}
      open={open}
      onOk={handleOk}
      onCancel={handleCancel}
      confirmLoading={delegateMutation.isPending}
      okText={t('workflow.delegation.delegateBtn')}
      cancelText={t('common.cancel')}
      destroyOnHidden
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="delegateToUserId"
          label={t('workflow.delegation.delegateToUserId')}
          rules={[{ required: true, message: t('workflow.delegation.requiredField') }]}
        >
          <Input placeholder={t('workflow.delegation.userIdPlaceholder')} />
        </Form.Item>
        <Form.Item
          name="expiresAt"
          label={t('workflow.delegation.expiresAt')}
          rules={[{ required: true, message: t('workflow.delegation.requiredField') }]}
        >
          <DatePicker showTime style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item name="reason" label={t('workflow.delegation.reason')}>
          <Input.TextArea rows={3} placeholder={t('workflow.delegation.reasonPlaceholder')} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
