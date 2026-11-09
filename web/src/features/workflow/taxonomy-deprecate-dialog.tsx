// Shared deprecate (soft-delete) dialog for workflow taxonomy admin pages.
// Enforces BE R7: confirmCode must match Code EXACTLY (case-sensitive) and reason ≥10 chars.

import { Modal, Form, Input, Alert } from 'antd';
import { useTranslation } from 'react-i18next';

interface Props {
  open: boolean;
  /** The Code being deprecated (read-only display + case-sensitive confirm target). */
  code: string;
  /** Optional busy state disables OK button while the mutation runs. */
  confirmLoading?: boolean;
  onConfirm: (payload: { confirmCode: string; reason: string }) => void | Promise<void>;
  onCancel: () => void;
}

interface FormValues {
  confirmCode: string;
  reason: string;
}

export function TaxonomyDeprecateDialog({ open, code, confirmLoading, onConfirm, onCancel }: Props) {
  const { t } = useTranslation();
  const [form] = Form.useForm<FormValues>();

  const handleOk = async () => {
    // validateFields() rejects on validation failure; antd Form already surfaces
    // the error in the UI, so we swallow the rejection here to avoid an
    // unhandled-promise-rejection leak (vitest v4 strict-mode flags it).
    let values: FormValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    await onConfirm({ confirmCode: values.confirmCode, reason: values.reason });
    form.resetFields();
  };

  const handleCancel = () => {
    form.resetFields();
    onCancel();
  };

  return (
    <Modal
      title={t('workflow.taxonomy.admin.deprecateTitle')}
      open={open}
      onOk={handleOk}
      onCancel={handleCancel}
      okText={t('workflow.taxonomy.admin.deprecate')}
      cancelText={t('common.cancel')}
      okButtonProps={{ danger: true, loading: confirmLoading }}
      destroyOnClose
    >
      <Alert
        type="warning"
        showIcon
        message={t('workflow.taxonomy.admin.deprecateWarning', { code })}
        style={{ marginBottom: 16 }}
      />
      <Form form={form} layout="vertical" requiredMark>
        <Form.Item
          name="confirmCode"
          label={t('workflow.taxonomy.admin.confirmCodeLabel', { code })}
          rules={[
            { required: true },
            {
              // Case-SENSITIVE match (plan R7 + BE handler uses StringComparison.Ordinal)
              validator: (_, value: string | undefined) =>
                value === code
                  ? Promise.resolve()
                  : Promise.reject(new Error(t('workflow.taxonomy.admin.confirmCodeMismatch'))),
            },
          ]}
        >
          <Input autoComplete="off" placeholder={code} />
        </Form.Item>

        <Form.Item
          name="reason"
          label={t('workflow.taxonomy.admin.reasonLabel')}
          rules={[
            { required: true },
            { min: 10, message: t('workflow.taxonomy.admin.reasonTooShort') },
            { max: 500 },
          ]}
        >
          <Input.TextArea rows={3} placeholder={t('workflow.taxonomy.admin.reasonPlaceholder')} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
