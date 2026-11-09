import { useEffect } from 'react';
import { Modal, Form, Input } from 'antd';
import { useTranslation } from 'react-i18next';

interface RejectEnrollmentModalProps {
  open: boolean;
  confirmLoading?: boolean;
  /** Emits the trimmed rejection reason. */
  onSubmit: (reason: string) => void;
  onCancel: () => void;
}

interface RejectFormValues {
  reason: string;
}

/**
 * Reject-with-reason modal for an enrollment. The reason is mandatory (BE persists it as the
 * RejectionReason the user later sees on their enrollment); re-enroll stays possible after reject (H3).
 */
export function RejectEnrollmentModal({ open, confirmLoading, onSubmit, onCancel }: RejectEnrollmentModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<RejectFormValues>();

  useEffect(() => {
    if (open) form.resetFields();
  }, [open, form]);

  async function handleOk() {
    try {
      const values = await form.validateFields();
      onSubmit(values.reason.trim());
    } catch {
      // validateFields rejects on empty reason — antd shows the field error, nothing else to do.
    }
  }

  return (
    <Modal
      open={open}
      title={t('signatureEnrollments.rejectTitle')}
      okText={t('signatureEnrollments.rejectConfirm')}
      okButtonProps={{ danger: true }}
      cancelText={t('common.cancel')}
      confirmLoading={confirmLoading}
      onOk={handleOk}
      onCancel={onCancel}
      destroyOnClose
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="reason"
          label={t('signatureEnrollments.rejectReasonLabel')}
          rules={[
            { required: true, message: t('signatureEnrollments.rejectReasonRequired') },
            { max: 500, message: t('signatureEnrollments.rejectReasonMax') },
          ]}
        >
          <Input.TextArea rows={3} maxLength={500} showCount placeholder={t('signatureEnrollments.rejectReasonPlaceholder')} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
